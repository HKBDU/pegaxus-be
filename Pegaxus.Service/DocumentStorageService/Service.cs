using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.DocumentStorageService;

public class Service : IService, IDisposable
{
    public const long MaxFileSize = 5 * 1024 * 1024;
    private readonly DocumentStorageOptions _options = new();
    private readonly ILogger<Service> _logger;
    private IAmazonS3? _client;
    private bool _privateBucketVerified;

    public Service(IConfiguration configuration, ILogger<Service> logger)
    {
        configuration.GetSection(nameof(DocumentStorageOptions)).Bind(_options);
        _logger = logger;
    }

    protected virtual IAmazonS3 CreateClient() => new AmazonS3Client(RegionEndpoint.GetBySystemName(_options.Region));
    private IAmazonS3 Client => _client ??= CreateClient();
    public void Dispose() => _client?.Dispose();

    public async Task ValidateContentAsync(MediaService.PreviewFile file, CancellationToken cancellationToken = default)
    {
        if (file.Content.Length > MaxFileSize) throw FileTooLarge();
        if (!MatchesFileSignature(file.Content, file.Content.Length, file.ContentType)) throw InvalidFormat();
        if (string.IsNullOrWhiteSpace(_options.ClamAvHost) || _options.ClamAvPort is < 1 or > 65535)
            throw new Pegaxus.Service.Exceptions.Exceptions("Document virus scanning must be configured.", 503, "DOCUMENT_STORAGE_NOT_CONFIGURED");
        using var content = new MemoryStream(file.Content);
        await ScanAsync(content, cancellationToken);
    }

    public static string ValidateFileMetadata(IFormFile file)
    {
        if (file is null)
            throw new BadRequestException("At least one non-empty document file is required.");
        return ValidateFileMetadata(file.FileName, file.ContentType, file.Length);
    }

    public static string ValidateFileMetadata(string fileName, string contentType, long sizeBytes)
    {
        if (sizeBytes <= 0)
            throw new BadRequestException("At least one non-empty document file is required.");
        if (sizeBytes > MaxFileSize)
            throw FileTooLarge();
        if (string.IsNullOrWhiteSpace(fileName))
            throw new BadRequestException("Document file name is invalid.");
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            throw new BadRequestException("Document file name is invalid.");
        var mimeType = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => throw InvalidFormat()
        };
        if (!string.Equals(contentType?.Trim(), mimeType, StringComparison.OrdinalIgnoreCase))
            throw InvalidFormat();
        return mimeType;
    }

    public async Task<StoredDocumentFile> UploadAsync(Guid versionId, int sortOrder, IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var mimeType = ValidateFileMetadata(file);
        EnsureConfigured();
        if (versionId == Guid.Empty || sortOrder is < 0 or > 9)
            throw new BadRequestException("Document version or file order is invalid.");
        await using var original = file.OpenReadStream();
        using var content = new MemoryStream();
        var chunk = new byte[65536];
        int bytesRead;
        while ((bytesRead = await original.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (content.Length + bytesRead > MaxFileSize)
                throw FileTooLarge();
            content.Write(chunk, 0, bytesRead);
        }
        if (content.Length != file.Length || !MatchesFileSignature(content.GetBuffer(), (int)content.Length, mimeType))
            throw InvalidFormat();
        content.Position = 0;
        await ScanAsync(content, cancellationToken);
        content.Position = 0;
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var key = $"documents/{versionId:N}/{sortOrder:D2}{Path.GetExtension(name).ToLowerInvariant()}";
        var storageUrl = $"s3://{_options.BucketName}/{key}";
        var uploadAttempted = false;
        try
        {
            await VerifyPrivateBucketAsync(cancellationToken);
            uploadAttempted = true;
            var response = await Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.BucketName, Key = key, InputStream = content, ContentType = mimeType,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256, IfNoneMatch = "*",
                AutoCloseStream = false, Headers = { CacheControl = "private, no-store" }
            }, cancellationToken);
            if (!string.IsNullOrEmpty(response.VersionId))
                storageUrl += "?versionId=" + Uri.EscapeDataString(response.VersionId);
            if (response.ServerSideEncryptionMethod != ServerSideEncryptionMethod.AES256)
                throw StorageUnavailable();
            return new StoredDocumentFile(storageUrl, name, mimeType, content.Length);
        }
        catch (Exception error)
        {
            if (uploadAttempted && error is not AmazonS3Exception { StatusCode: HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict })
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await DeleteAsync(storageUrl, cleanup.Token);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogError(cleanupError, "Failed to remove uncommitted document object {Key}", key);
                }
            }
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested || error is Pegaxus.Service.Exceptions.Exceptions)
                throw;
            _logger.LogError(error, "Secure document storage upload failed");
            throw StorageUnavailable();
        }
    }

    public async Task<MediaService.PreviewFile> DownloadPreviewAsync(string storageUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var location = StorageLocation(storageUrl);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            timeout.Token.ThrowIfCancellationRequested();
            await VerifyPrivateBucketAsync(timeout.Token);
            using var response = await Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName, Key = location.Key, VersionId = location.VersionId
            }, timeout.Token);
            if (response.ContentLength > MaxFileSize) throw FileTooLarge();
            using var content = new MemoryStream();
            var chunk = new byte[65536];
            int bytesRead;
            while ((bytesRead = await response.ResponseStream.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (content.Length + bytesRead > MaxFileSize) throw FileTooLarge();
                content.Write(chunk, 0, bytesRead);
            }
            var bytes = content.ToArray();
            var mimeType = new[] { "application/pdf", "image/jpeg", "image/png" }
                .FirstOrDefault(type => MatchesFileSignature(bytes, bytes.Length, type)) ?? throw InvalidFormat();
            return new MediaService.PreviewFile(bytes, mimeType);
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException("Document file not found.", "DOCUMENT_FILE_NOT_FOUND");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new Pegaxus.Service.Exceptions.Exceptions("Document preview timed out.", 504, "FILE_STORAGE_TIMEOUT");
        }
        catch (Exception error)
        {
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested || error is Pegaxus.Service.Exceptions.Exceptions)
                throw;
            _logger.LogError(error, "Secure document preview failed");
            throw StorageUnavailable();
        }
    }

    public async Task<SignedDocumentUrl> CreateReadUrlAsync(string storageUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var location = StorageLocation(storageUrl);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await VerifyPrivateBucketAsync(cancellationToken);
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
            var url = await Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName, Key = location.Key, VersionId = location.VersionId, Verb = HttpVerb.GET,
                Protocol = Protocol.HTTPS, Expires = expiresAt.UtcDateTime
            });
            cancellationToken.ThrowIfCancellationRequested();
            return new SignedDocumentUrl(url, expiresAt);
        }
        catch (Exception error) when (error is not OperationCanceledException && error is not Pegaxus.Service.Exceptions.Exceptions)
        {
            _logger.LogError(error, "Secure document URL signing failed");
            throw StorageUnavailable();
        }
    }

    public async Task DeleteAsync(string storageUrl, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var location = StorageLocation(storageUrl);
        await Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName, Key = location.Key, VersionId = location.VersionId
        }, cancellationToken);
    }

    private async Task VerifyPrivateBucketAsync(CancellationToken cancellationToken)
    {
        if (_privateBucketVerified) return;
        var result = await Client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest { BucketName = _options.BucketName }, cancellationToken);
        var block = result.PublicAccessBlockConfiguration;
        if (block is null || block.BlockPublicAcls != true || block.IgnorePublicAcls != true ||
            block.BlockPublicPolicy != true || block.RestrictPublicBuckets != true)
            throw new Pegaxus.Service.Exceptions.Exceptions("Document storage bucket must block public access.", 503, "INSECURE_DOCUMENT_STORAGE");
        _privateBucketVerified = true;
    }

    private async Task ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var socket = new TcpClient();
            await socket.ConnectAsync(_options.ClamAvHost, _options.ClamAvPort, timeout.Token);
            await using var stream = socket.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var chunk = new byte[65536];
            var length = new byte[4];
            int bytesRead;
            while ((bytesRead = await content.ReadAsync(chunk, timeout.Token)) != 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, bytesRead);
                await stream.WriteAsync(length, timeout.Token);
                await stream.WriteAsync(chunk.AsMemory(0, bytesRead), timeout.Token);
            }
            await stream.WriteAsync(new byte[4], timeout.Token);
            using var response = new MemoryStream();
            var singleByte = new byte[1];
            var terminated = false;
            while (response.Length < 1024 && await stream.ReadAsync(singleByte, timeout.Token) != 0)
            {
                if (singleByte[0] == 0) { terminated = true; break; }
                response.WriteByte(singleByte[0]);
            }
            var verdict = Encoding.UTF8.GetString(response.ToArray());
            if (verdict.EndsWith(" FOUND", StringComparison.Ordinal))
                throw new BadRequestException("Malware detected in uploaded file.", "MALWARE_DETECTED");
            if (!terminated || verdict != "stream: OK")
                throw new Pegaxus.Service.Exceptions.Exceptions("Document virus scanning is unavailable.", 503, "VIRUS_SCANNER_UNAVAILABLE");
        }
        catch (Exception error) when (error is not Pegaxus.Service.Exceptions.Exceptions &&
            !(error is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            _logger.LogError(error, "Document virus scan failed");
            throw new Pegaxus.Service.Exceptions.Exceptions("Document virus scanning is unavailable.", 503, "VIRUS_SCANNER_UNAVAILABLE");
        }
    }

    private (string Key, string? VersionId) StorageLocation(string storageUrl)
    {
        if (!Uri.TryCreate(storageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "s3" ||
            uri.Host != _options.BucketName || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !uri.AbsolutePath.StartsWith("/documents/", StringComparison.Ordinal))
            throw new BadRequestException("Document storage reference is invalid.");
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.Count > 1 || query.Count == 1 && (!query.TryGetValue("versionId", out var versions) ||
            versions.Count != 1 || string.IsNullOrWhiteSpace(versions[0])))
            throw new BadRequestException("Document storage reference is invalid.");
        return (uri.AbsolutePath.TrimStart('/'), query.TryGetValue("versionId", out var version) ? version[0] : null);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.BucketName) || string.IsNullOrWhiteSpace(_options.Region) ||
            string.IsNullOrWhiteSpace(_options.ClamAvHost) || _options.ClamAvPort is < 1 or > 65535)
            throw new Pegaxus.Service.Exceptions.Exceptions("Secure document storage and virus scanning must be configured.", 503, "DOCUMENT_STORAGE_NOT_CONFIGURED");
    }

    private static bool MatchesFileSignature(byte[] content, int length, string mimeType) => mimeType switch
    {
        "application/pdf" => content.AsSpan(0, length).StartsWith("%PDF-"u8),
        "image/jpeg" => content.AsSpan(0, length).StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
        "image/png" => content.AsSpan(0, length).StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
        _ => false
    };

    private static Pegaxus.Service.Exceptions.Exceptions FileTooLarge() => new(
        "File size exceeds the maximum limit of 5MB. Please compress your file.", 413, "FILE_TOO_LARGE");
    private static Pegaxus.Service.Exceptions.Exceptions InvalidFormat() => new(
        "Invalid file format. Only PDF, JPG, JPEG, and PNG files are accepted for DocumentVersion.", 415, "INVALID_FILE_TYPE");
    private static Pegaxus.Service.Exceptions.Exceptions StorageUnavailable() => new(
        "Secure document storage is unavailable.", 503, "DOCUMENT_STORAGE_UNAVAILABLE");
}
