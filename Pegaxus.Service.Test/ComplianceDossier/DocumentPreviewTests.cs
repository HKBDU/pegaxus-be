using System.Net;
using Microsoft.Extensions.Configuration;
using Pegaxus.Service.Exceptions;
using CloudinaryPreviewService = Pegaxus.Service.CloudinaryService.Service;

namespace Pegaxus.Service.Test;

public class DocumentPreviewTests
{
    private const string FileUrl = "https://res.cloudinary.com/test-cloud/raw/upload/ehc.pdf";

    [Fact]
    public async Task AuthenticatedPreview_SupportsTenMegabytesAndUsesExpiringPrivateDownload()
    {
        var bytes = new byte[10 * 1024 * 1024];
        "%PDF-1.7"u8.CopyTo(bytes);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var result = await CreateService(handler).DownloadPreviewAsync(
            "https://res.cloudinary.com/test-cloud/image/authenticated/v1/evidence/account/asset.pdf");
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(bytes, result.Content);
        Assert.Equal("api.cloudinary.com", new Uri(handler.LastUrl!).Host);
        Assert.Contains("expires_at=", handler.LastUrl);
        Assert.Contains("signature=", handler.LastUrl);
    }

    [Theory]
    [InlineData("http://res.cloudinary.com/test-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://localhost/test-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://res.cloudinary.com.attacker.com/test-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://res.cloudinary.com/another-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://res.cloudinary.com/test-cloud/video/upload/ehc.pdf")]
    [InlineData("https://res.cloudinary.com/test-cloud/raw/fetch/ehc.pdf")]
    [InlineData("https://res.cloudinary.com:8443/test-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://user@res.cloudinary.com/test-cloud/raw/upload/ehc.pdf")]
    [InlineData("https://res.cloudinary.com/test-cloud/raw/upload/ehc.pdf#fragment")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not-a-url")]
    public async Task UntrustedStorageUrls_AreRejectedBeforeNetworkAccess(string url)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var error = await Assert.ThrowsAsync<BadRequestException>(() => CreateService(handler).DownloadPreviewAsync(url));
        Assert.Equal("INVALID_FILE_URL", error.ErrorCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PdfPreview_DetectsMimeFromBytesInsteadOfUntrustedHeaders()
    {
        var bytes = "%PDF-1.7 content"u8.ToArray();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var result = await CreateService(handler).DownloadPreviewAsync(FileUrl);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(bytes, result.Content);
        Assert.Equal(FileUrl, handler.LastUrl);
    }

    [Theory]
    [InlineData("iVBORw0KGgo=", "image/png")]
    [InlineData("/9j/", "image/jpeg")]
    [InlineData("R0lGODlh", "image/gif")]
    [InlineData("UklGRgAAAABXRUJQ", "image/webp")]
    public async Task Images_ArePreviewedWithTheirActualMimeType(string base64, string expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Convert.FromBase64String(base64)) });
        Assert.Equal(expected, (await CreateService(handler).DownloadPreviewAsync(FileUrl)).ContentType);
    }

    [Theory]
    [InlineData(302, 502)]
    [InlineData(404, 404)]
    [InlineData(500, 502)]
    public async Task RedirectsAndStorageErrors_AreNotReturnedAsFiles(int storageStatus, int expectedStatus)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)storageStatus));
        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(handler).DownloadPreviewAsync(FileUrl));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedFiles_AreRejectedEvenWithoutContentLength(bool withoutContentLength)
    {
        var bytes = new byte[5 * 1024 * 1024 + 1];
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = withoutContentLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes)
        });
        var error = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(handler).DownloadPreviewAsync(FileUrl));
        Assert.Equal(413, error.StatusCode);
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("")]
    public async Task ActiveOrUnknownContent_IsRejected(string content)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        var error = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(handler).DownloadPreviewAsync(FileUrl));
        Assert.Equal(415, error.StatusCode);
    }

    [Fact]
    public async Task NetworkFailure_IsTranslatedWithoutLeakingStorageDetails()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("sensitive storage details"));
        var error = await Assert.ThrowsAsync<Pegaxus.Service.Exceptions.Exceptions>(() => CreateService(handler).DownloadPreviewAsync(FileUrl));
        Assert.Equal(502, error.StatusCode);
        Assert.DoesNotContain("sensitive", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedIntoAStorageError()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(handler).DownloadPreviewAsync(FileUrl, cancellation.Token));
    }

    private static CloudinaryPreviewService CreateService(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CloudinaryOptions:CloudName"] = "test-cloud",
            ["CloudinaryOptions:ApiKey"] = "test-key",
            ["CloudinaryOptions:ApiSecret"] = "test-secret"
        }).Build();
        return new CloudinaryPreviewService(configuration, new PreviewClientFactory(handler));
    }

    private sealed class PreviewClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(CloudinaryPreviewService.PreviewHttpClient, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUrl { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }
}
