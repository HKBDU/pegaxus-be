using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service
{
    public async Task<Response.PhotosResponse> AddPhotosAsync(Guid transportId, Guid horseHealthLogId,
        Request.AddPhotosRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, photosValidator);
        if (transportId == Guid.Empty || horseHealthLogId == Guid.Empty)
            throw new BadRequestException("Transport and health log IDs must be non-empty UUIDs.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
                // Match recording/lifecycle lock order; re-read the photo count after acquiring both locks.
                await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, cancellationToken);
                var escortId = await GetEscortAsync(accountId, cancellationToken);
                await GetTransportBookingAsync(transportId, escortId, cancellationToken);
                await db.LockRowsAsync(db.HorseHealthLogs.Where(item => item.TransportId == transportId && item.HorseHealthLogId == horseHealthLogId),
                    item => item.HorseHealthLogId, cancellationToken);
                var log = await db.HorseHealthLogs.SingleOrDefaultAsync(item => item.TransportId == transportId &&
                    item.HorseHealthLogId == horseHealthLogId, cancellationToken)
                    ?? throw new NotFoundException("Horse health log not found", "HEALTH_LOG_NOT_FOUND");
                await db.Entry(log).ReloadAsync(cancellationToken);
                if (log.TransportId != transportId)
                    throw new NotFoundException("Horse health log not found", "HEALTH_LOG_NOT_FOUND");
                if (log.EmployeeId != escortId) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
                if (log.PhotoUrls.Length + request.Files.Count > 5)
                    throw new UnprocessableEntityException("A health log may have at most 5 photos.", "PHOTO_LIMIT_EXCEEDED");

                var urls = new List<string>(log.PhotoUrls);
                foreach (var file in request.Files)
                {
                    var url = await ConfirmImageAsync(accountId, file.PublicId, cancellationToken);
                    if (urls.Contains(url, StringComparer.Ordinal)) throw InvalidImage();
                    urls.Add(url);
                }
                var photos = await SignPhotosAsync(urls, cancellationToken);
                log.PhotoUrls = urls.ToArray();
                log.UpdatedAt = Truncate(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(photos);
            }
            catch (Exception error) when (PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 2)
                {
                    db.ChangeTracker.Clear();
                    continue;
                }
                throw new ConflictException("The record changed. Please retry the request", "CONCURRENT_UPDATE");
            }
        }
    }

    private async Task<IReadOnlyList<Response.Photo>> SignPhotosAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        var photos = new List<Response.Photo>();
        foreach (var url in urls)
        {
            var signed = url.StartsWith("s3://", StringComparison.Ordinal)
                ? await documentStorage.CreateReadUrlAsync(url, cancellationToken)
                : await media.CreateReadUrlAsync(url, cancellationToken);
            photos.Add(new(signed.Url));
        }
        return photos;
    }

    private async Task<string> ConfirmImageAsync(Guid accountId, string publicId, CancellationToken cancellationToken)
    {
        MediaService.ConfirmedUploadResult asset;
        try { asset = await media.ConfirmUploadAsync(accountId, publicId, cancellationToken); }
        catch (Pegaxus.Service.Exceptions.Exceptions error) when (error.ErrorCode is "FILE_TOO_LARGE" or "INVALID_FILE_TYPE")
        { throw InvalidImage(); }
        if (asset.MimeType is not ("image/jpeg" or "image/png") ||
            asset.SizeBytes <= 0 || asset.SizeBytes > DocumentStorageService.Service.MaxFileSize) throw InvalidImage();
        var image = await media.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (image.Content.LongLength != asset.SizeBytes || image.ContentType != asset.MimeType ||
            !Images.IsValid(image.Content, asset.MimeType)) throw InvalidImage();
        return asset.FileUrl;
    }

    private static BadRequestException InvalidImage() =>
        new("Only JPG, JPEG, PNG images up to 5MB are accepted. Please try again.", "INVALID_IMAGE");
}
