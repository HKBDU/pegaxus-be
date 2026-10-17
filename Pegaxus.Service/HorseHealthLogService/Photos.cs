using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

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
            var photoIds = new List<Guid>();
            var committed = false;
            var commitAttempted = false;
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

                var images = new List<PreviewFile>();
                foreach (var file in request.Files) images.Add(await ReadImageAsync(file, cancellationToken));
                var urls = new List<string>(log.PhotoUrls);
                foreach (var image in images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var photoId = Guid.NewGuid();
                    photoIds.Add(photoId);
                    urls.Add(await media.UploadPrivateImageAsync(photoId, image, cancellationToken));
                }
                var photos = await SignPhotosAsync(urls, cancellationToken);
                log.PhotoUrls = urls.ToArray();
                log.UpdatedAt = Truncate(clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                committed = true;
                return new(photos);
            }
            catch (Exception error)
            {
                var conflict = PostgresErrors.IsWriteConflict(error);
                if (!committed && (!commitAttempted || conflict))
                    foreach (var photoId in photoIds) await CleanupPhotoAsync(photoId);
                if (!committed && commitAttempted && !conflict)
                    logger.LogError(error, "Health photo commit outcome is unknown; retaining photos {PhotoIds}", photoIds);
                if (!committed && conflict && attempt < 2)
                {
                    db.ChangeTracker.Clear();
                    continue;
                }
                if (conflict) throw new ConflictException("The record changed. Please retry the request", "CONCURRENT_UPDATE");
                throw;
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

    private static async Task<PreviewFile> ReadImageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        string mimeType;
        try { mimeType = DocumentStorageService.Service.ValidateFileMetadata(file); }
        catch (Pegaxus.Service.Exceptions.Exceptions) { throw InvalidImage(); }
        if (mimeType is not ("image/jpeg" or "image/png")) throw InvalidImage();
        await using var input = file.OpenReadStream();
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (content.Length + count > DocumentStorageService.Service.MaxFileSize) throw InvalidImage();
            content.Write(buffer, 0, count);
        }
        var bytes = content.ToArray();
        if (bytes.Length != file.Length || !Images.IsValid(bytes, mimeType)) throw InvalidImage();
        return new(bytes, mimeType);
    }

    private async Task CleanupPhotoAsync(Guid photoId)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await media.DeletePrivateImageAsync(photoId, cleanup.Token);
        }
        catch (Exception error) { logger.LogError(error, "Failed to remove uncommitted health photo {PhotoId}", photoId); }
    }

    private static BadRequestException InvalidImage() =>
        new("Only JPG, JPEG, PNG images up to 5MB are accepted. Please try again.", "INVALID_IMAGE");
}
