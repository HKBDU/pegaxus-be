using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.MediaService;

public static class AvatarImages
{
    /// <summary>Confirms an avatar upload of the account and checks the stored file matches what Cloudinary reported, then returns its public URL.</summary>
    public static async Task<string> ConfirmAvatarImageAsync(this IMediaService media, Guid accountId, string publicId, CancellationToken cancellationToken)
    {
        var asset = await media.ConfirmAvatarAsync(accountId, publicId, cancellationToken);
        var content = await media.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
        if (content.Content.Length != asset.SizeBytes || content.ContentType != asset.MimeType)
            throw new BadRequestException($"Image must be JPG/PNG and no larger than {Limits.UploadMaxMegabytes} MB", "INVALID_FILE");
        return asset.FileUrl;
    }
}
