using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.Models;
using Pegaxus.Service.NotificationService;
using Request = Pegaxus.Service.InvoiceService.Request;

namespace Pegaxus.Service.Test;

public class InvoiceIssueHttpTests
{
    [Fact]
    public async Task Http_ValidationErrorsAndSuccess_MatchContract()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await InvoiceIssueTests.SeedAsync(db);
        await using var app = await StartAsync(db);
        using var client = Client(app, actor.AccountId, "LogisticsManager");
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/issue";
        foreach (var payload in new object[] { new { }, new { lastUpdatedAt = (string?)null }, new { lastUpdatedAt = "invalid-date" } })
        {
            var error = await Error(await client.PostAsJsonAsync(url, payload), 400, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), x => x.GetProperty("field").GetString()!.Contains("lastUpdatedAt"));
        }
        var stale = await Error(await client.PostAsJsonAsync(url, new { lastUpdatedAt = "2026-11-05T07:00:00Z" }), 409, "INVOICE_MODIFIED");
        Assert.Equal("The invoice was just modified. Please reload the page", stale.GetProperty("message").GetString());
        invoice.TotalAmount = 0; await db.SaveChangesAsync();
        await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 422, "INVOICE_TOTAL_NOT_POSITIVE");
        invoice.TotalAmount = 120; await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice issued successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(invoice.InvoiceId, value.GetProperty("invoiceId").GetGuid());
        Assert.Equal(invoice.InvoiceCode, value.GetProperty("invoiceNumber").GetString());
        Assert.Equal("DEPOSIT", value.GetProperty("type").GetString()); Assert.Equal("ISSUED", value.GetProperty("status").GetString());
        Assert.Equal("2026-11-05", value.GetProperty("invoiceDate").GetString());
        Assert.Equal(InvoiceIssueTests.TestClock.GetUtcNow(), value.GetProperty("issuedAt").GetDateTimeOffset());
        Assert.Equal(InvoiceIssueTests.TestClock.GetUtcNow(), value.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(120, value.GetProperty("grandTotal").GetDecimal()); Assert.Equal("EUR", value.GetProperty("currency").GetString());
        Assert.Equal("2026-11-15", value.GetProperty("dueDate").GetString());
        Assert.Equal(new[] { "RECORD_PAYMENT", "CANCEL", "CREATE_ANOTHER" }, value.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 409, "INVALID_INVOICE_STATUS");
        await Error(await client.PostAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/issue", InvoiceIssueTests.Version(invoice)), 404, "INVOICE_NOT_FOUND");
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyActiveManagerCanIssue_IncludingForgedRole(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, _) = await InvoiceIssueTests.SeedAsync(db);
        var employee = actor.EmployeeAccount!; employee.Role = role; await db.SaveChangesAsync();
        await using var app = await StartAsync(db);
        using var client = Client(app, actor.AccountId, JwtServices.GetRoleName(role));
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/issue";
        Assert.Equal(expected, (int)(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice))).StatusCode);
        if (role != EmployeeRole.LOGISTICS_MANAGER)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
            await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 403, "FORBIDDEN");
            InvoiceIssueTests.AssertNoEffects(db);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, customer.AccountId, "Customer"));
        await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 403, "FORBIDDEN");
        client.DefaultRequestHeaders.Authorization = null;
        await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 401, "UNAUTHORIZED");
        employee.IsActive = false; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
        await Error(await client.PostAsJsonAsync(url, InvoiceIssueTests.Version(invoice)), 401, "UNAUTHORIZED");
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-tests", ["JwtOptions:Audience"] = "invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration); builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<TimeProvider>(InvoiceIssueTests.TestClock);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(x => x.Value!.Errors.Count > 0).SelectMany(x => x.Value!.Errors.Select(e => new {
                    field = string.IsNullOrEmpty(x.Key) ? "$" : char.ToLowerInvariant(x.Key[0]) + x.Key[1..], message = e.ErrorMessage })),
                "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IService, Pegaxus.Service.InvoiceService.Service>();
        builder.Services.AddScoped<Pegaxus.Service.PaymentGatewayService.IService, InvoiceOnlinePaymentTests.Gateway>();
        builder.Services.AddScoped<IValidator<Request.IssueRequest>, IssueRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.RecordPaymentRequest>, RecordPaymentRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CancelRequest>, CancelRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.BookingListRequest>, BookingListRequestValidator>();
        builder.Services.AddScoped<Pegaxus.Service.NotificationService.IService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        return app;
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
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString());
        return body;
    }
}
