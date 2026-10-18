namespace Pegaxus.Service.MediaService;

public interface IMediaService
{
    Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default);
    Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default);
    Task<SignedUploadResult> CreateSignedUploadAsync(
        Guid accountId,
        string entity,
        string contentType,
        long sizeBytes);
    Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default);
    Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default);
    Task<DocumentStorageService.SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default);
    Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default);
}

public sealed record PreviewFile(byte[] Content, string ContentType);

public sealed record SignedUploadResult(
    string PublicId,
    string UploadUrl,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record ConfirmedUploadResult(
    string PublicId,
    string FileUrl,
    string FileName,
    string MimeType,
    long SizeBytes);
