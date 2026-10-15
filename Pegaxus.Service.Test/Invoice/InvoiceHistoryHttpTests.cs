using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;

namespace Pegaxus.Service.Test;

public class InvoiceHistoryHttpTests
{
    [Fact]
    public async Task Http_HistoryEnvelopeAndNullableActors_MatchContract()
    {
        await using var db = TestSupport.CreateDb(); var (manager, customer, invoice, histories) = await InvoiceHistoryTests.SeedAsync(db);
        await using var app = await StartAsync(db); using var client = Client(app, manager.AccountId, "LogisticsManager");
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/status-history";
        var response = await client.GetAsync(url); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice status history retrieved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value"); Assert.Equal(JsonValueKind.Array, value.ValueKind); Assert.Equal(4, value.GetArrayLength());
        Assert.Equal(histories.Select(x => x.InvoiceStatusHistoryId), value.EnumerateArray().Select(x => x.GetProperty("historyId").GetGuid()));
        var first = value[0]; Assert.Equal("CREATE", first.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("previousStatus").ValueKind); Assert.Equal("DRAFT", first.GetProperty("newStatus").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("reason").ValueKind);
        Assert.Equal(histories[0].ChangedByAccountId, first.GetProperty("changedBy").GetProperty("accountId").GetGuid());
        Assert.Equal("Le Hoang Phuc", first.GetProperty("changedBy").GetProperty("fullName").GetString());
        Assert.Equal("LOGISTICS_MANAGER", first.GetProperty("changedBy").GetProperty("role").GetString());
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(histories[0].ChangedAt, DateTimeKind.Utc)), first.GetProperty("changedAt").GetDateTimeOffset());
        foreach (var system in value.EnumerateArray().Skip(2))
        {
            Assert.Equal(JsonValueKind.Null, system.GetProperty("changedBy").GetProperty("accountId").ValueKind);
            Assert.Equal("System", system.GetProperty("changedBy").GetProperty("fullName").GetString());
            Assert.Equal(JsonValueKind.Null, system.GetProperty("changedBy").GetProperty("role").ValueKind);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, customer.AccountId, "Customer"));
        var own = (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("value");
        Assert.Equal(3, own.GetArrayLength()); Assert.Equal("ISSUE", own[0].GetProperty("type").GetString());
        Assert.Equal("DRAFT", own[0].GetProperty("previousStatus").GetString());
        Assert.DoesNotContain(own.EnumerateArray(), x => x.GetProperty("newStatus").GetString() == "DRAFT" || x.GetProperty("type").GetString() == "CREATE");
    }

    [Fact]
    public async Task Http_OwnershipUnissuedEmptyHistoryAndInactiveSessions_HaveExpectedResponses()
    {
        await using var db = TestSupport.CreateDb(); var (manager, customer, invoice, histories) = await InvoiceHistoryTests.SeedAsync(db);
        var stranger = await TestSupport.AddCustomerAsync(db);
        await using var app = await StartAsync(db); using var client = Client(app, customer.AccountId, "Customer");
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/status-history";
        var missing = await Error(await client.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}/status-history"), 404, "INVOICE_NOT_FOUND");
        Assert.Equal("Invoice not found", missing.GetProperty("message").GetString());
        db.InvoiceStatusHistories.RemoveRange(histories); await db.SaveChangesAsync();
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("value").EnumerateArray());
        invoice.IssuedAt = null; invoice.Status = InvoiceStatus.CANCELLED; await db.SaveChangesAsync();
        await Error(await client.GetAsync(url), 403, "FORBIDDEN");
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, stranger.Account.AccountId, "Customer"));
        await Error(await client.GetAsync(url), 404, "INVOICE_NOT_FOUND");
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, manager.AccountId, "LogisticsManager"));
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("value").EnumerateArray());
        client.DefaultRequestHeaders.Authorization = null; await Error(await client.GetAsync(url), 401, "UNAUTHORIZED");
        manager.EmployeeAccount!.IsActive = false; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, manager.AccountId, "LogisticsManager"));
        await Error(await client.GetAsync(url), 401, "UNAUTHORIZED");
        customer.CustomerAccount!.IsDeleted = true; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, customer.AccountId, "Customer"));
        await Error(await client.GetAsync(url), 401, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Http_OtherEmployeesAndForgedClaims_CannotReadHistory(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var (manager, _, invoice, _) = await InvoiceHistoryTests.SeedAsync(db);
        manager.EmployeeAccount!.Role = role; await db.SaveChangesAsync();
        await using var app = await StartAsync(db); using var client = Client(app, manager.AccountId, JwtServices.GetRoleName(role));
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/status-history";
        await Error(await client.GetAsync(url), 403, "FORBIDDEN");
        foreach (var claim in new[] { "LogisticsManager", "Customer" })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, manager.AccountId, claim));
            await Error(await client.GetAsync(url), 403, "FORBIDDEN");
        }
    }
    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-tests", ["JwtOptions:Audience"] = "invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration); builder.Services.AddSingleton(db);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IInvoiceService>(_ => InvoiceHistoryTests.CreateService(db));
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); return app;
    }
    private static string Token(WebApplication app, Guid id, string role) => new JwtServices(app.Configuration).GenerateAccessToken(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)]);
    private static HttpClient Client(WebApplication app, Guid id, string role)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, id, role)); return client;
    }
    private static async Task<JsonElement> Error(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString()); return body;
    }
}
