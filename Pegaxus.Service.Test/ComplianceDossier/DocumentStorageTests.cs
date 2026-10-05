using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
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
    public async Task CleanFile_IsScannedEncryptedAndSignedForFiveMinutes()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var content = "%PDF-1.7 content"u8.ToArray();
        var versionId = Guid.NewGuid();
        var stored = await service.UploadAsync(versionId, 0, File("passport.pdf", "application/pdf", content));
        Assert.Equal(content, await scanned.WaitAsync(TimeSpan.FromSeconds(5)));
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
    [InlineData("stream: Eicar-Test-Signature FOUND", 400, "MALWARE_DETECTED")]
    [InlineData("stream: INSTREAM size limit exceeded. ERROR", 503, "VIRUS_SCANNER_UNAVAILABLE")]
    [InlineData("unknown", 503, "VIRUS_SCANNER_UNAVAILABLE")]
    public async Task UnsafeOrFailedScan_NeverUploadsToStorage(string verdict, int statusCode, string errorCode)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, verdict);
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(statusCode, error.StatusCode);
        Assert.Equal(errorCode, error.ErrorCode);
        Assert.Empty(client.Uploads);
        Assert.Equal(0, client.BucketChecks);
    }

    [Theory]
    [InlineData("passport.pdf", "application/pdf", "<html>not a PDF</html>")]
    [InlineData("passport.jpg", "image/jpeg", "%PDF-1.7 content")]
    [InlineData("passport.exe", "application/pdf", "%PDF-1.7 content")]
    [InlineData("passport.png", "application/zip", "%PDF-1.7 content")]
    public async Task ExtensionMimeAndActualContent_MustAgree(string name, string mime, string content)
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client, 1);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File(name, mime, Encoding.UTF8.GetBytes(content))));
        Assert.Equal(415, error.StatusCode);
        Assert.Empty(client.Uploads);
        Assert.Equal(0, client.BucketChecks);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg", "/9j/")]
    [InlineData("png", "image/png", "iVBORw0KGgo=")]
    public async Task AllowedImages_AreScannedAndUploaded(string extension, string mime, string base64)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File($"page.{extension}", mime, Convert.FromBase64String(base64)));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(mime, stored.MimeType);
        Assert.Single(client.Uploads);
    }

    [Fact]
    public async Task PublicBucket_IsRejectedBeforeObjectCreation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client { IsPrivate = false };
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("INSECURE_DOCUMENT_STORAGE", error.ErrorCode);
        Assert.Empty(client.Uploads);
    }

    [Fact]
    public async Task MissingEncryptionAcknowledgment_RemovesTheNewObject()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client { AcknowledgeEncryption = false };
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7 content"u8.ToArray())));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
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
    public async Task TenMegabytes_IsAcceptedAfterFullScan()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var bytes = new byte[StorageService.MaxFileSize];
        "%PDF-1.7"u8.CopyTo(bytes);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File("passport.pdf", "application/pdf", bytes));
        Assert.Equal(bytes.Length, (await scanned.WaitAsync(TimeSpan.FromSeconds(5))).Length);
        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(bytes.Length, client.Content!.Length);
    }

    [Fact]
    public async Task VersionedObjects_KeepTheirExactStorageVersionForReadAndCleanup()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client { ObjectVersionId = "s3-object-1" };
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var stored = await service.UploadAsync(Guid.NewGuid(), 0, File("passport.pdf", "application/pdf", "%PDF-1.7"u8.ToArray()));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.EndsWith("?versionId=s3-object-1", stored.StorageUrl);
        Assert.Contains("versionId=s3-object-1", (await service.CreateReadUrlAsync(stored.StorageUrl)).Url);
        await service.DeleteAsync(stored.StorageUrl);
        Assert.Equal("s3-object-1", Assert.Single(client.Deletes).VersionId);
    }

    [Fact]
    public async Task ExistingObjectCollision_NeverDeletesThePreviousObject()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK");
        var client = new RecordingS3Client { UploadFailureStatus = HttpStatusCode.PreconditionFailed };
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(client.Deletes);
    }

    [Fact]
    public async Task ScannerConnectionEndingWithoutTerminator_IsRejected()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var scanned = RunScannerAsync(listener, "stream: OK", terminate: false);
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.UploadAsync(Guid.NewGuid(), 0,
            File("passport.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())));
        await scanned.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("VIRUS_SCANNER_UNAVAILABLE", error.ErrorCode);
        Assert.Empty(client.Uploads);
    }

    [Fact]
    public async Task Scanner_DoesNotDependOnTheCallerSynchronizationContext()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var context = new TrackingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        Task<byte[]> scanned;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            scanned = RunScannerAsync(listener, "stream: OK");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        var client = new RecordingS3Client();
        using var service = CreateService(client, ((IPEndPoint)listener.LocalEndpoint).Port);
        var content = "%PDF-1.7"u8.ToArray();
        await service.UploadAsync(Guid.NewGuid(), 0, File("passport.pdf", "application/pdf", content));
        Assert.Equal(content, await scanned.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, context.PostCount);
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            base.Post(callback, state);
        }
    }

    private static IFormFile File(string name, string mime, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "Files", name) { Headers = new HeaderDictionary(), ContentType = mime };

    private static TestStorageService CreateService(RecordingS3Client client, int scannerPort)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentStorageOptions:BucketName"] = "private-bucket",
            ["DocumentStorageOptions:Region"] = "us-east-1",
            ["DocumentStorageOptions:ClamAvHost"] = "127.0.0.1",
            ["DocumentStorageOptions:ClamAvPort"] = scannerPort.ToString()
        }).Build();
        return new TestStorageService(configuration, client);
    }

    private static async Task<byte[]> RunScannerAsync(TcpListener listener, string verdict, bool terminate = true)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
        await using var stream = socket.GetStream();
        var command = new byte[10];
        await stream.ReadExactlyAsync(command, timeout.Token).ConfigureAwait(false);
        Assert.Equal("zINSTREAM\0", Encoding.UTF8.GetString(command));
        using var content = new MemoryStream();
        var lengthBytes = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(lengthBytes, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
            if (length == 0) break;
            Assert.InRange(length, 1, 65536);
            var chunk = new byte[length];
            await stream.ReadExactlyAsync(chunk, timeout.Token).ConfigureAwait(false);
            content.Write(chunk);
        }
        await stream.WriteAsync(Encoding.UTF8.GetBytes(verdict + (terminate ? "\0" : "")), timeout.Token).ConfigureAwait(false);
        return content.ToArray();
    }

    [Fact]
    public async Task Preview_ReadsExactPrivateObjectVersionAndSupportsTenMegabytes()
    {
        var bytes = new byte[StorageService.MaxFileSize];
        "%PDF-1.7"u8.CopyTo(bytes);
        var client = new RecordingS3Client { PreviewContent = bytes };
        using var service = CreateService(client, 3310);
        var result = await service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf?versionId=old%2F1");
        Assert.Equal(bytes, result.Content);
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
        using var service = CreateService(client, 3310);
        Assert.Equal(mimeType, (await service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf")).ContentType);
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("")]
    public async Task Preview_RejectsActiveOrUnknownContent(string content)
    {
        var client = new RecordingS3Client { PreviewContent = Encoding.UTF8.GetBytes(content) };
        using var service = CreateService(client, 3310);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(415, error.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_RejectsOversizedFilesIncludingMissingLength(bool missingLength)
    {
        var client = new RecordingS3Client { PreviewContent = new byte[StorageService.MaxFileSize + 1], PreviewContentLength = missingLength ? 0 : StorageService.MaxFileSize + 1 };
        using var service = CreateService(client, 3310);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(413, error.StatusCode);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(500, 503)]
    public async Task Preview_TranslatesStorageErrorsWithoutLeakingDetails(int storageStatus, int expectedStatus)
    {
        var client = new RecordingS3Client { DownloadFailureStatus = (HttpStatusCode)storageStatus };
        using var service = CreateService(client, 3310);
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.DoesNotContain("sensitive", error.Message);
    }

    [Fact]
    public async Task Preview_DeniesForeignBucketAndPublicBucketBeforeDownloading()
    {
        var client = new RecordingS3Client { IsPrivate = false };
        using var service = CreateService(client, 3310);
        await Assert.ThrowsAsync<BadRequestException>(() => service.DownloadPreviewAsync("s3://another-bucket/documents/version/00.pdf"));
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => service.DownloadPreviewAsync("s3://private-bucket/documents/version/00.pdf"));
        Assert.Equal(503, error.StatusCode);
        Assert.Empty(client.Downloads);
    }

    [Fact]
    public async Task Preview_PropagatesCallerCancellation()
    {
        var client = new RecordingS3Client();
        using var service = CreateService(client, 3310);
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
