using System.Net;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.Service.Exceptions;

using StorageService = Pegaxus.Service.DocumentStorageService.Service;

namespace Pegaxus.Service.Test;

public class DocumentStorageTests
{
    [Fact]
    public async Task DirectUploadContent_IsValidatedWithoutStorageConfiguration()
    {
        using var service = new StorageService(new ConfigurationBuilder().Build(), NullLogger<StorageService>.Instance);
        var bytes = "%PDF-1.7 content"u8.ToArray();
        await service.ValidateContentAsync(new Pegaxus.Service.MediaService.PreviewFile(bytes, "application/pdf"));
    }

    [Theory]
    [InlineData(0, 415)]
    [InlineData(10, 415)]
    [InlineData(StorageService.MaxFileSize + 1, 413)]
    public async Task DirectUploadContent_RejectsInvalidOrOversizedContent(long size, int statusCode)
    {
        using var service = new StorageService(new ConfigurationBuilder().Build(), NullLogger<StorageService>.Instance);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.ValidateContentAsync(
            new Pegaxus.Service.MediaService.PreviewFile(new byte[size], "application/pdf")));
        Assert.Equal(statusCode, error.StatusCode);
    }

    [Fact]
    public async Task ValidFile_IsEncryptedAndSignedForFiveMinutes()
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client);
        var content = "%PDF-1.7 content"u8.ToArray();
        var versionId = Guid.NewGuid();
        var stored = await service.UploadAsync(versionId, 0, File("passport.pdf", "application/pdf", content));
        var put = Assert.Single(client.Uploads);
        Assert.Equal(ServerSideEncryptionMethod.AES256, put.ServerSideEncryptionMethod);
        Assert.Equal("*", put.IfNoneMatch);
        Assert.Equal("private, no-store", put.Headers.CacheControl);
        Assert.Equal($"documents/{versionId:N}/00.pdf", put.Key);
        Assert.Equal(content, client.Content);
        Assert.StartsWith("s3://private-bucket/documents/", stored.StorageUrl);
        var signed = await service.CreateReadUrlAsync(stored.StorageUrl);
        Assert.StartsWith("https://", signed.Url);
        Assert.Contains("X-Amz-Signature=", signed.Url);
        Assert.InRange(signed.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        Assert.Equal(1, client.BucketChecks);
        await service.DeleteAsync(stored.StorageUrl);
        Assert.Equal(put.Key, Assert.Single(client.Deletes).Key);
    }

    [Theory]
    [InlineData("passport.pdf", "application/pdf", "<html>not a PDF</html>")]
    [InlineData("passport.jpg", "image/jpeg", "%PDF-1.7 content")]
    [InlineData("passport.exe", "application/pdf", "%PDF-1.7 content")]
    [InlineData("passport.png", "application/zip", "%PDF-1.7 content")]
    public async Task ExtensionMimeAndActualContent_MustAgree(string name, string mime, string content)
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File(name, mime, Encoding.UTF8.GetBytes(content))));
        Assert.Equal(415, error.StatusCode);
        Assert.Empty(client.Uploads);
        Assert.Equal(0, client.BucketChecks);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg", "/9j/")]
    [InlineData("png", "image/png", "iVBORw0KGgo=")]
    public async Task AllowedImages_AreUploaded(string extension, string mime, string base64)
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File($"page.{extension}", mime, Convert.FromBase64String(base64)));
        Assert.Equal(mime, stored.MimeType);
        Assert.Single(client.Uploads);
    }

    [Fact]
    public async Task PublicBucket_IsRejectedBeforeObjectCreation()
    {
        var client = new RecordingS3Client { IsPrivate = false };
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        Assert.Equal("INSECURE_DOCUMENT_STORAGE", error.ErrorCode);
        Assert.Empty(client.Uploads);
    }

    [Fact]
    public async Task MissingEncryptionAcknowledgment_RemovesTheNewObject()
    {
        var client = new RecordingS3Client { AcknowledgeEncryption = false };
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        Assert.Equal("DOCUMENT_STORAGE_UNAVAILABLE", error.ErrorCode);
        Assert.Single(client.Deletes);
    }

    [Fact]
    public async Task MissingConfiguration_FailsClosed()
    {
        using var service = new StorageService(new ConfigurationBuilder().Build(), NullLogger<StorageService>.Instance);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal("DOCUMENT_STORAGE_NOT_CONFIGURED", error.ErrorCode);
    }

    [Fact]
    public async Task FiveMegabytes_IsAccepted()
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client);
        var bytes = new byte[StorageService.MaxFileSize];
        "%PDF-1.7"u8.CopyTo(bytes);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File("passport.pdf", "application/pdf", bytes));
        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(bytes.Length, client.Content!.Length);
    }

    [Fact]
    public async Task VersionedObjects_KeepTheirExactStorageVersionForReadAndCleanup()
    {
        var client = new RecordingS3Client { ObjectVersionId = "s3-object-1" };
        using var service = CreateService(client);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File("passport.pdf", "application/pdf", "%PDF-1.7"u8.ToArray()));
        Assert.EndsWith("?versionId=s3-object-1", stored.StorageUrl);
        Assert.Contains("versionId=s3-object-1", (await service.CreateReadUrlAsync(stored.StorageUrl)).Url);
        await service.DeleteAsync(stored.StorageUrl);
        Assert.Equal("s3-object-1", Assert.Single(client.Deletes).VersionId);
    }

    [Fact]
    public async Task ExistingObjectCollision_NeverDeletesThePreviousObject()
    {
        var client = new RecordingS3Client { UploadFailureStatus = HttpStatusCode.PreconditionFailed };
        using var service = CreateService(client);
        await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())));
        Assert.Empty(client.Deletes);
    }

    private static IFormFile File(string name, string mime, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "Files", name) { Headers = new HeaderDictionary(), ContentType = mime };

    private static TestStorageService CreateService(RecordingS3Client client)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentStorageOptions:BucketName"] = "private-bucket",
            ["DocumentStorageOptions:Region"] = "us-east-1"
        }).Build();
        return new TestStorageService(configuration, client);
    }

    [Fact]
    public async Task Preview_ReadsExactPrivateObjectVersionAndSupportsFiveMegabytes()
    {
        var bytes = new byte[StorageService.MaxFileSize];
        "%PDF-1.7"u8.CopyTo(bytes);
        var client = new RecordingS3Client { PreviewContent = bytes };
        using var service = CreateService(client);
        var result = await service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf?versionId=old%2F1");
        Assert.True(bytes.AsSpan().SequenceEqual(result.Content));
        Assert.Equal("application/pdf", result.ContentType);
        var download = Assert.Single(client.Downloads);
        Assert.Equal("private-bucket", download.BucketName);
        Assert.Equal("documents/version/00.pdf", download.Key);
        Assert.Equal("old/1", download.VersionId);
        Assert.Equal(1, client.BucketChecks);
    }

    [Theory]
    [InlineData("iVBORw0KGgo=", "image/png")]
    [InlineData("/9j/", "image/jpeg")]
    public async Task Preview_DetectsActualImageType(string content, string mimeType)
    {
        var client = new RecordingS3Client { PreviewContent = Convert.FromBase64String(content) };
        using var service = CreateService(client);
        Assert.Equal(mimeType, (await service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf")).ContentType);
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("")]
    public async Task Preview_RejectsActiveOrUnknownContent(string content)
    {
        var client = new RecordingS3Client { PreviewContent = Encoding.UTF8.GetBytes(content) };
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(415, error.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_RejectsOversizedFilesIncludingMissingLength(bool missingLength)
    {
        var client = new RecordingS3Client { PreviewContent = new byte[StorageService.MaxFileSize + 1], PreviewContentLength = missingLength ? 0 : StorageService.MaxFileSize + 1 };
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(413, error.StatusCode);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(500, 503)]
    public async Task Preview_TranslatesStorageErrorsWithoutLeakingDetails(int storageStatus, int expectedStatus)
    {
        var client = new RecordingS3Client { DownloadFailureStatus = (HttpStatusCode)storageStatus };
        using var service = CreateService(client);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.DoesNotContain("sensitive", error.Message);
    }

    [Fact]
    public async Task Preview_DeniesForeignBucketAndPublicBucketBeforeDownloading()
    {
        var client = new RecordingS3Client { IsPrivate = false };
        using var service = CreateService(client);
        await Assert.ThrowsAsync<BadRequestException>(() => service.DownloadPreviewAsync("s3://another-bucket/documents/version/00.pdf"));
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(503, error.StatusCode);
        Assert.Empty(client.Downloads);
    }

    [Fact]
    public async Task Preview_PropagatesCallerCancellation()
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf", cancellation.Token));
        Assert.Empty(client.Downloads);
    }

    private sealed class TestStorageService(IConfiguration configuration, RecordingS3Client client)
        : StorageService(configuration, NullLogger<StorageService>.Instance)
    {
        protected override IAmazonS3 CreateClient() => client;
    }

    private sealed class RecordingS3Client() : AmazonS3Client(new BasicAWSCredentials("test-key", "test-secret"), RegionEndpoint.USEast1)
    {
        public List<PutObjectRequest> Uploads { get; } = [];
        public List<DeleteObjectRequest> Deletes { get; } = [];
        public List<GetObjectRequest> Downloads { get; } = [];
        public byte[] PreviewContent { get; init; } = "%PDF-1.7"u8.ToArray();
        public long? PreviewContentLength { get; init; }
        public HttpStatusCode? DownloadFailureStatus { get; init; }
        public byte[]? Content { get; private set; }
        public int BucketChecks { get; private set; }
        public bool IsPrivate { get; init; } = true;
        public bool AcknowledgeEncryption { get; init; } = true;
        public string? ObjectVersionId { get; init; }
        public HttpStatusCode? UploadFailureStatus { get; init; }
        public override Task<GetPublicAccessBlockResponse> GetPublicAccessBlockAsync(GetPublicAccessBlockRequest request,
            CancellationToken cancellationToken = default)
        {
            BucketChecks++;
            return Task.FromResult(new GetPublicAccessBlockResponse
            {
                PublicAccessBlockConfiguration = new PublicAccessBlockConfiguration
                {
                    BlockPublicAcls = IsPrivate, IgnorePublicAcls = IsPrivate, BlockPublicPolicy = IsPrivate, RestrictPublicBuckets = IsPrivate
                }
            });
        }
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Uploads.Add(request);
            if (UploadFailureStatus.HasValue)
                throw new AmazonS3Exception("Planned storage failure") { StatusCode = UploadFailureStatus.Value };
            using var content = new MemoryStream();
            await request.InputStream.CopyToAsync(content, cancellationToken);
            Content = content.ToArray();
            return new PutObjectResponse
            {
                ServerSideEncryptionMethod = AcknowledgeEncryption ? ServerSideEncryptionMethod.AES256 : null, VersionId = ObjectVersionId
            };
        }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Downloads.Add(request);
            if (DownloadFailureStatus.HasValue)
                throw new AmazonS3Exception("sensitive storage details") { StatusCode = DownloadFailureStatus.Value };
            return Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(PreviewContent), ContentLength = PreviewContentLength ?? PreviewContent.Length
            });
        }
        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
        {
            Deletes.Add(request);
            return Task.FromResult(new DeleteObjectResponse());
        }
    }
}
