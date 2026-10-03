using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.DocumentStorageService;

public interface IService
{
    Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file, CancellationToken cancellationToken = default);
    Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default);
}

public sealed record StoredDocumentFile(string StorageUrl, string FileName, string MimeType, long SizeBytes);
public sealed record SignedDocumentUrl(string Url, DateTimeOffset ExpiresAt);

public sealed class DocumentStorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string ClamAvHost { get; set; } = string.Empty;
    public int ClamAvPort { get; set; } = 3310;
}
