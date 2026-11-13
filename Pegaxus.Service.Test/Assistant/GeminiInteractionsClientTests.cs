using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Pegaxus.Service.Assistant;
using Pegaxus.Service.Assistant.Gemini;
using Pegaxus.Service.Assistant.Tools;

namespace Pegaxus.Service.Test.Assistant;

public class GeminiInteractionsClientTests
{
    [Fact]
    public async Task CreateAsync_SendsStatelessFunctionToolsAndParsesSteps()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(async (request, _) =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"steps":[{"type":"thought","signature":"keep-me"},{"type":"function_call","id":"call-1","name":"customer_list_my_bookings","arguments":{}},{"type":"model_output","content":[{"type":"text","text":"Bookings found."}]}]}
                    """, Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler);
        var client = new GeminiInteractionsClient(http, Options.Create(new AssistantOptions
        {
            ApiKey = "test-secret",
            Model = "gemini-test"
        }));
        var input = new JsonArray(new JsonObject { ["type"] = "user_input", ["content"] = "question" });
        var result = await client.CreateAsync(input, [], CancellationToken.None);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", captured!.RequestUri!.ToString());
        Assert.Equal("test-secret", captured.Headers.GetValues("x-goog-api-key").Single());
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("gemini-test", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("Bookings found.", result.OutputText);
        Assert.Equal("keep-me", result.Steps[0]!["signature"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateAsync_MapsProviderFailureWithoutReturningProviderBody()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("private provider response")
        }));
        using var http = new HttpClient(handler);
        var client = new GeminiInteractionsClient(http, Options.Create(new AssistantOptions { ApiKey = "key", Model = "model" }));

        var error = await Assert.ThrowsAsync<GeminiUnavailableException>(() => client.CreateAsync(new JsonArray(), [], CancellationToken.None));

        Assert.Equal("The assistant provider could not complete the request.", error.Message);
        Assert.DoesNotContain("private", error.Message);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
