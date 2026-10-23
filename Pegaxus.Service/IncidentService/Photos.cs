using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private async Task AddPhotosAsync(IncidentReport incident, VehicleIncidentDetail detail,
        IReadOnlyList<UploadService.Request.UploadedAssetReference> files, Guid accountId, DateTime now, CancellationToken ct)
    {
        if (files.Count == 0) return;
        var existing = await db.IncidentPhotos.AsNoTracking().Where(photo => photo.IncidentReportId == incident.IncidentReportId)
            .Select(photo => new { photo.PhotoUrl, photo.SortOrder }).ToArrayAsync(ct);
        var urls = existing.Select(photo => photo.PhotoUrl).ToHashSet(StringComparer.Ordinal);
        var order = existing.Select(photo => photo.SortOrder).DefaultIfEmpty(0).Max();
        foreach (var file in files)
        {
            MediaService.ConfirmedUploadResult asset;
            try { asset = await media.ConfirmUploadAsync(accountId, file.PublicId, ct); }
            catch (Pegaxus.Service.Exceptions.Exceptions error) when (error.ErrorCode is "FILE_TOO_LARGE" or "INVALID_FILE_TYPE")
            { throw InvalidImage(); }
            if (asset.MimeType is not ("image/jpeg" or "image/png") || asset.SizeBytes <= 0 || asset.SizeBytes > DocumentStorageService.Service.MaxFileSize)
                throw InvalidImage();
            var image = await media.DownloadPreviewAsync(asset.FileUrl, ct);
            if (image.Content.LongLength != asset.SizeBytes || image.ContentType != asset.MimeType || !Images.IsValid(image.Content, asset.MimeType) ||
                !urls.Add(asset.FileUrl)) throw InvalidImage();
            // Presigned evidence already exists; a failed transaction must not delete an upload used by another report.
            db.IncidentPhotos.Add(new IncidentPhoto
            {
                IncidentPhotoId = Guid.NewGuid(), IncidentReport = incident, VehicleIncidentDetail = detail,
                PhotoUrl = asset.FileUrl, SortOrder = ++order, CreatedAt = now
            });
        }
    }

    private static BadRequestException InvalidImage() => new(
        "Only JPG, JPEG, PNG images up to 5MB are accepted. Please try again.", "INVALID_IMAGE");

    private async Task<string[]> AddPhotosAsync(IncidentReport incident, IReadOnlyList<UploadService.Request.UploadedAssetReference> files,
        Guid accountId, DateTime now, CancellationToken ct, EmergencyCostRequest? cost = null)
    {
        if (files.Count == 0) return [];
        var costId = cost?.EmergencyCostRequestId;
        var existing = await db.IncidentPhotos.AsNoTracking().Where(photo => photo.IncidentReportId == incident.IncidentReportId && photo.EmergencyCostRequestId == costId)
            .Select(photo => new { photo.PhotoUrl, photo.SortOrder }).ToArrayAsync(ct);
        var urls = existing.Select(photo => photo.PhotoUrl).ToHashSet(StringComparer.Ordinal);
        var order = existing.Select(photo => photo.SortOrder).DefaultIfEmpty(0).Max();
        var added = new List<string>();
        foreach (var file in files)
        {
            var asset = await media.ConfirmUploadAsync(accountId, file.PublicId, ct);
            if (asset.SizeBytes > DocumentStorageService.Service.MaxFileSize)
                throw new Pegaxus.Service.Exceptions.Exceptions("Each photo must not exceed 5 MB", 413, "FILE_TOO_LARGE");
            if (asset.MimeType is not ("image/jpeg" or "image/png"))
                throw new BadRequestException("Only JPG, JPEG and PNG images are accepted", "INVALID_FILE_TYPE");
            var image = await media.DownloadPreviewAsync(asset.FileUrl, ct);
            if (asset.SizeBytes <= 0 || image.Content.LongLength != asset.SizeBytes || image.ContentType != asset.MimeType ||
                !Images.IsValid(image.Content, asset.MimeType) || !urls.Add(asset.FileUrl))
                throw new BadRequestException("The uploaded photo is invalid", "INVALID_IMAGE");
            // Presigned evidence already exists; rollback must not delete an upload used by another report.
            db.IncidentPhotos.Add(new IncidentPhoto
            {
                IncidentPhotoId = Guid.NewGuid(), IncidentReport = incident, EmergencyCostRequest = cost,
                PhotoUrl = asset.FileUrl, SortOrder = ++order, CreatedAt = now
            });
            added.Add(asset.FileUrl);
        }
        return added.ToArray();
    }
}
