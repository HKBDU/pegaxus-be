using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.HorseHealthLogService;

public sealed partial class Service
{
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
