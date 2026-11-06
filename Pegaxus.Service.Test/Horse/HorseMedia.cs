using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.Test.HorseTests;

/// <summary>Stands in for Cloudinary: an avatar publicId is confirmed as a PNG of <see cref="Content"/>, unless the content is changed to mismatch it.</summary>
internal sealed class HorseMedia : IMediaService
{
    internal byte[] Content { get; set; } = [1, 2, 3];
    internal Guid? ConfirmedFor { get; private set; }

    internal static string UrlFor(string publicId) => "https://res.cloudinary.com/t/image/upload/v1/" + publicId + ".png";

    public Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default)
    {
        ConfirmedFor = accountId;
        return Task.FromResult(new ConfirmedUploadResult(publicId, UrlFor(publicId), "a.png", "image/png", 3));
    }

    public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(new PreviewFile(Content, "image/png"));
    public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
    public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Pegaxus.Service.DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
