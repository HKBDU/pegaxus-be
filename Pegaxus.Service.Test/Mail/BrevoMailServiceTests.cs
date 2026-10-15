using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

using Mail = Pegaxus.Service.MailService;

namespace Pegaxus.Service.Test;

public class BrevoMailServiceTests
{
    private const string ApiKey = "xkeysib-test-key";

    private static IConfiguration Config(Action<Dictionary<string, string?>>? change = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Brevo:ApiUrl"] = "https://api.brevo.com/v3/smtp/email", ["Brevo:ApiKey"] = ApiKey,
            ["Brevo:SenderName"] = "Pegaxus", ["Brevo:SenderEmail"] = "noreply@pegaxus.example"
        };
        change?.Invoke(values);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static Mail.MailContent Content(string to = "customer@example.com") =>
        new() { To = to, Subject = "Booking approved", Body = "<p>Hello</p>" };

    [Fact]
    public async Task SendMail_PostsTheBrevoPayload_WithApiKeyHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        await new Mail.Service(Config(), new Factory(handler)).SendMail(Content());

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request.RequestUri!.ToString());
        Assert.Equal(ApiKey, Assert.Single(handler.Request.Headers.GetValues("api-key")));
        Assert.Contains("application/json", handler.Request.Headers.Accept.ToString());
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("Pegaxus", body.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("noreply@pegaxus.example", body.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("customer@example.com", body.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Booking approved", body.GetProperty("subject").GetString());
        Assert.Equal("<p>Hello</p>", body.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task SendMail_OmitsSenderName_WhenBlank()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        await new Mail.Service(Config(x => x["Brevo:SenderName"] = ""), new Factory(handler)).SendMail(Content());
        Assert.False(JsonDocument.Parse(handler.Body!).RootElement.GetProperty("sender").TryGetProperty("name", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SendMail_Throws_WhenBrevoRejectsTheEmail_WithoutLeakingTheKey(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status, "{\"code\":\"unauthorized\",\"message\":\"Key not found\"}");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new Mail.Service(Config(), new Factory(handler)).SendMail(Content()));
        Assert.Equal(status, error.StatusCode);
        Assert.Contains(((int)status).ToString(), error.Message);
        Assert.Contains("Key not found", error.Message);
        Assert.DoesNotContain(ApiKey, error.Message);
    }

    [Theory]
    [InlineData("Brevo:ApiKey", "")]
    [InlineData("Brevo:SenderEmail", "")]
    [InlineData("Brevo:ApiUrl", "http://api.brevo.com/v3/smtp/email")]
    [InlineData("Brevo:ApiUrl", "not a url")]
    public async Task SendMail_Throws_WhenNotConfigured_WithoutCallingBrevo(string key, string value)
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new Mail.Service(Config(x => x[key] = value), new Factory(handler)).SendMail(Content()));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task SendMail_Throws_ForInvalidRecipient_WithoutCallingBrevo()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created);
        await Assert.ThrowsAsync<FormatException>(() =>
            new Mail.Service(Config(), new Factory(handler)).SendMail(Content("not-an-email")));
        Assert.Null(handler.Request);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response = "{\"messageId\":\"<1@brevo>\"}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
