using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Extensions;

namespace Pegaxus.Service.Test;

public class ForwardedHeadersTests
{
    private static async Task<string> SchemeSeenByTheApiAsync(Action<HttpRequestMessage>? configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddProxyForwardedHeaders();
        await using var app = builder.Build();
        app.UseForwardedHeaders();
        app.MapGet("/scheme", (HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host}");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/scheme");
        configure?.Invoke(request);
        var text = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        await app.StopAsync();
        return text;
    }

    [Fact]
    public async Task BehindAProxy_TheRequestSchemeIsTheOneTheProxyForwarded()
    {
        var seen = await SchemeSeenByTheApiAsync(request => request.Headers.Add("X-Forwarded-Proto", "https"));

        Assert.StartsWith("https://", seen);
    }

    [Fact]
    public async Task WithoutForwardedHeaders_TheSchemeIsUnchanged()
    {
        var seen = await SchemeSeenByTheApiAsync(null);

        Assert.StartsWith("http://", seen);
    }
}
