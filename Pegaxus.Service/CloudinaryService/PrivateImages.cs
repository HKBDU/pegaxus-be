using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.CloudinaryService;

public partial class Service
{
    public async Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default)
    {
        if (imageId == Guid.Empty || !Images.IsValid(image.Content, image.ContentType))
            throw new BadRequestException("Only JPG, JPEG, PNG images up to 5MB are accepted. Please try again.", "INVALID_IMAGE");
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();
        var publicId = $"health/{imageId:N}";
        using var content = new MemoryStream(image.Content, writable: false);
        try
        {
            var result = await UploadPrivateImageCoreAsync(new ImageUploadParams
            {
                File = new FileDescription(image.ContentType == "image/png" ? "photo.png" : "photo.jpg", content),
                PublicId = publicId, Type = "authenticated", Overwrite = false
            }, cancellationToken);
            if (result.Error is not null || result.SecureUrl is null || result.PublicId != publicId)
                throw new Pegaxus.Service.Exceptions.Exceptions("Image storage is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
            var location = StorageLocation(result.SecureUrl.ToString());
            if (location.PublicId != publicId || location.ResourceType != "image" || location.DeliveryType != "authenticated")
                throw new Pegaxus.Service.Exceptions.Exceptions("Image storage is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
            return result.SecureUrl.ToString();
        }
        catch (Exception error) when (error is HttpRequestException or BadRequestException)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Image storage is unavailable", 502, "FILE_STORAGE_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Image upload timed out", 504, "FILE_STORAGE_TIMEOUT");
        }
    }

    public async Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default)
    {
        if (imageId == Guid.Empty) throw new BadRequestException("Image ID must be a non-empty UUID.", "INVALID_IMAGE");
        EnsureConfigured();
        cancellationToken.ThrowIfCancellationRequested();
        var result = await DeletePrivateImageCoreAsync(new DeletionParams($"health/{imageId:N}")
        {
            ResourceType = ResourceType.Image, Type = "authenticated"
        }, cancellationToken);
        if (result.Error is not null || result.Result is not ("ok" or "not found"))
            throw new Pegaxus.Service.Exceptions.Exceptions("Image cleanup failed", 502, "FILE_STORAGE_UNAVAILABLE");
    }

    protected virtual Task<ImageUploadResult> UploadPrivateImageCoreAsync(ImageUploadParams parameters, CancellationToken cancellationToken) =>
        _cloudinary.UploadAsync(parameters, cancellationToken);

    protected virtual Task<DeletionResult> DeletePrivateImageCoreAsync(DeletionParams parameters, CancellationToken cancellationToken) =>
        _cloudinary.DestroyAsync(parameters).WaitAsync(cancellationToken);
}
