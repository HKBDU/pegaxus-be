using CloudinaryDotNet.Actions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.CloudinaryService;

public partial class Service
{
    private const long MaxAvatarSize = 2 * 1024 * 1024;

    // Avatars are shown in plain <img> tags across the app, so unlike attachments they are delivered publicly.
    private SignedUploadResult CreateAvatarUpload(Guid accountId, string contentType, long sizeBytes)
    {
        if (sizeBytes <= 0 || sizeBytes > MaxAvatarSize)
            throw new Pegaxus.Service.Exceptions.Exceptions("Image must be greater than 0 and must not exceed 2 MB", 413, "FILE_TOO_LARGE");
        if (contentType?.Trim().ToLowerInvariant() is not ("image/jpeg" or "image/png"))
            throw new BadRequestException("Image must be JPG/PNG and no larger than 2 MB", "INVALID_FILE_TYPE");
        EnsureConfigured();
        return SignUpload($"avatars/{accountId:D}/{Guid.NewGuid():N}", "upload", "jpg,jpeg,png");
    }

    public async Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicId) || publicId.Length > 255 ||
            !publicId.StartsWith($"avatars/{accountId:D}/", StringComparison.Ordinal) ||
            publicId.Split('/').Any(segment => segment is "" or "." or ".." || segment.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))))
            throw new BadRequestException("The publicId does not belong to the authenticated account", "INVALID_PUBLIC_ID");

        EnsureConfigured();
        GetResourceResult result;
        try
        {
            result = await GetResourceAsync(new GetResourceParams(publicId) { ResourceType = ResourceType.Image, Type = "upload" }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary confirmation timed out", 504, "FILE_STORAGE_TIMEOUT");
        }
        if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new NotFoundException("Uploaded file was not found on Cloudinary", "UPLOAD_NOT_FOUND");
        if (result.Error is not null || string.IsNullOrWhiteSpace(result.SecureUrl?.ToString()))
            throw new Pegaxus.Service.Exceptions.Exceptions("Cloudinary is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
        if (result.PublicId != publicId || result.Type != "upload" || result.ResourceType != ResourceType.Image)
            throw new BadRequestException("Uploaded file metadata is invalid", "INVALID_FILE_TYPE");
        if (result.Bytes <= 0 || result.Bytes > MaxAvatarSize)
            throw new Pegaxus.Service.Exceptions.Exceptions("Image must not be empty or exceed 2 MB", 413, "FILE_TOO_LARGE");
        var mimeType = result.Format?.ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            _ => throw new BadRequestException("Image must be JPG/PNG and no larger than 2 MB", "INVALID_FILE_TYPE")
        };
        var fileUrl = result.SecureUrl;
        var location = StorageLocation(fileUrl);
        if (location.DeliveryType != "upload" || location.ResourceType != "image")
            throw new BadRequestException("Uploaded file storage URL is invalid", "INVALID_FILE_URL");
        return new ConfirmedUploadResult(publicId, fileUrl, Path.GetFileName(publicId), mimeType, result.Bytes);
    }
}
