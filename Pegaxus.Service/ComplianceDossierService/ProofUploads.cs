using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    private static void ValidateProofReferences(List<UploadService.Request.UploadedAssetReference> files)
    {
        if (files.Any(file => file is null || string.IsNullOrWhiteSpace(file.PublicId)) ||
            files.Select(file => file.PublicId).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new BadRequestException("Provide distinct non-empty publicIds for all proof files.", "INVALID_PUBLIC_ID");
    }

    private async Task<ConfirmedUploadResult> ConfirmProofImageAsync(Guid accountId, string publicId, CancellationToken cancellationToken)
    {
        ConfirmedUploadResult asset;
        try
        {
            asset = await _mediaService.ConfirmUploadAsync(accountId, publicId, cancellationToken);
        }
        catch (Pegaxus.Service.Exceptions.Exceptions exception) when (exception.ErrorCode == "FILE_TOO_LARGE")
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Proof image must not exceed 5 MB", 413, "FILE_TOO_LARGE");
        }
        if (asset.SizeBytes > 5 * 1024 * 1024)
            throw new Pegaxus.Service.Exceptions.Exceptions("Proof image must not exceed 5 MB", 413, "FILE_TOO_LARGE");
        var extension = Path.GetExtension(asset.FileName).ToLowerInvariant();
        if (asset.SizeBytes <= 0 || extension is not (".jpg" or ".jpeg" or ".png") ||
            asset.MimeType is not ("image/jpeg" or "image/png"))
            throw new BadRequestException("Proof file must be JPG, JPEG, or PNG", "INVALID_FILE_TYPE");
        var content = await _mediaService.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (content.Content.Length != asset.SizeBytes || content.ContentType != asset.MimeType)
            throw new BadRequestException("Uploaded file content does not match its metadata.", "INVALID_FILE_TYPE");
        await _documentStorage.ValidateContentAsync(content, cancellationToken);
        return asset;
    }

    // Authority files may be PDFs, so they cannot use the image-only proof check above.
    private async Task<ConfirmedUploadResult> ConfirmAuthorityFileAsync(Guid accountId, UploadService.Request.UploadedAssetReference reference,
        string label, CancellationToken cancellationToken)
    {
        ConfirmedUploadResult asset;
        try
        {
            asset = await _mediaService.ConfirmUploadAsync(accountId, reference.PublicId, cancellationToken);
        }
        catch (Pegaxus.Service.Exceptions.Exceptions exception) when (exception.ErrorCode == "FILE_TOO_LARGE")
        {
            throw new Pegaxus.Service.Exceptions.Exceptions($"{label} must not exceed 2 MB", 413, "FILE_TOO_LARGE");
        }
        if (asset.SizeBytes > MaxProofFileSize)
            throw new Pegaxus.Service.Exceptions.Exceptions($"{label} must not exceed 2 MB", 413, "FILE_TOO_LARGE");
        var content = await _mediaService.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (content.Content.Length != asset.SizeBytes || content.ContentType != asset.MimeType)
            throw new BadRequestException("Uploaded file content does not match its metadata.", "INVALID_FILE_TYPE");
        await _documentStorage.ValidateContentAsync(content, cancellationToken);
        return asset;
    }
}
