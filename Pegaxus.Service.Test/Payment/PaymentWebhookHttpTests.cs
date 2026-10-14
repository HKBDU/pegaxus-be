using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Middleware;
using Pegaxus.API.Models;
using Pegaxus.Service.PaymentService;

namespace Pegaxus.Service.Test;

public class PaymentWebhookHttpTests
{
    [Fact]
    public async Task PublicEndpoint_RequiresSePaySecret_ParsesStringAmounts_AndAcknowledgesReplay()
    {
        await using var db = TestSupport.CreateDb();
        var receipt = await PaymentWebhookTests.SeedAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IPaymentService>(PaymentWebhookTests.CreateService(db));
        builder.Services.AddControllers().AddApplicationPart(typeof(PaymentsController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
                new BadRequestObjectResult(ResponseBuilder.ErrorResponse(null, "The request data is invalid")));
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        await using var host = builder.Build();
        host.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        host.UseAuthentication(); host.UseAuthorization(); host.MapControllers();
        await host.StartAsync();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        var payload = JsonSerializer.Serialize(PaymentWebhookTests.Payload(receipt)).Replace("100000", "\"100000.00\"");
        using var missing = await Post(client, payload);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("INVALID_SIGNATURE", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        client.DefaultRequestHeaders.Add("X-Secret-Key", PaymentWebhookTests.Secret);
        client.DefaultRequestHeaders.Add("X-Secret-Key", "wrong");
        using var duplicateHeaders = await Post(client, payload);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateHeaders.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Secret-Key");
        client.DefaultRequestHeaders.Add("X-Secret-Key", PaymentWebhookTests.Secret);
        using var pendingMismatch = await Post(client, payload.Replace("100000.00", "90000.00"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pendingMismatch.StatusCode);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var response = await Post(client, payload);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, body.GetProperty("code").GetInt32());
            Assert.Equal("Webhook processed successfully", body.GetProperty("message").GetString());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("value").ValueKind);
        }
        using var invalid = await Post(client, "{\"timestamp\": 1, \"order\": {\"order_amount\": \"NaN\"}}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unknown = await Post(client, payload.Replace(receipt.ReceiptCode, "RCP-UNKNOWN"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var mismatch = await Post(client, payload.Replace("100000.00", "90000.00"));
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode); // Confirmed receipt is already acknowledged.
        Assert.Single(db.InvoiceStatusHistories); Assert.Single(db.Notifications);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string payload) =>
        client.PostAsync("/api/v1/payments/webhook", new StringContent(payload, Encoding.UTF8, "application/json"));
}
