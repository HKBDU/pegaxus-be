using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.DocumentStorageService;

public interface IDocumentStorageService
{
    Task ValidateContentAsync(MediaService.PreviewFile file, CancellationToken cancellationToken = default);
    Task<MediaService.PreviewFile> DownloadPreviewAsync(string storageUrl, CancellationToken cancellationToken = default);
    Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default);
    Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default);

    /// <summary>Stores a generated audit package under <c>exports/</c>. Unlike documents there is no 5 MB or file type limit; the stream must be seekable.</summary>
    Task<StoredDocumentFile> UploadExportAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken = default);

    /// <summary>A download link for a stored audit package that stays valid until <paramref name="expiresAt"/>.</summary>
    Task<SignedDocumentUrl> CreateExportUrlAsync(string storageUrl, string fileName, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
}

public sealed record StoredDocumentFile(string StorageUrl, string FileName, string MimeType, long SizeBytes);
public sealed record SignedDocumentUrl(string Url, DateTimeOffset ExpiresAt);

public sealed class DocumentStorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}
