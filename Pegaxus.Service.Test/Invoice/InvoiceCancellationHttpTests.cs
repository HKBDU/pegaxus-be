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
using Pegaxus.API.Models;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.NotificationService;

using Request = Pegaxus.Service.InvoiceService.Request;

namespace Pegaxus.Service.Test;

public class InvoiceCancellationHttpTests
{
    [Fact]
    public async Task Http_CancellationValidationErrorsAndSuccess_MatchContract()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await InvoiceCancellationTests.SeedAsync(db);
        await using var app = await StartAsync(db); using var client = Client(app, actor.AccountId, "LogisticsManager");
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/cancel";
        foreach (var body in new object[] { new { }, new { cancellationReason = (string?)null },
                     new { cancellationReason = " 123456789 " }, new { cancellationReason = 12345 } })
        {
            var error = await Error(await client.PostAsJsonAsync(url, body), 400, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), x => x.GetProperty("field").GetString()!.Contains("cancellationReason"));
        }
        await Error(await client.PostAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/cancel", InvoiceCancellationTests.ValidRequest()), 404, "INVOICE_NOT_FOUND");
        var pending = InvoiceCancellationTests.AddReceipt(db, invoice, ReceiptStatus.PENDING); await db.SaveChangesAsync();
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 409, "PAYMENT_IN_PROGRESS");
        pending.Status = ReceiptStatus.CONFIRMED; await db.SaveChangesAsync();
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 422, "INVOICE_HAS_PAYMENT");
        db.Receipts.Remove(pending); invoice.Status = InvoiceStatus.PAID; await db.SaveChangesAsync();
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 422, "INVOICE_HAS_PAYMENT");
        InvoiceCancellationTests.AssertNoEffects(db);
        invoice.Status = InvoiceStatus.OVERDUE; await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, result.GetProperty("code").GetInt32());
        Assert.Equal("Invoice cancelled successfully", result.GetProperty("message").GetString());
        var value = result.GetProperty("value");
        Assert.Equal(invoice.InvoiceId, value.GetProperty("invoiceId").GetGuid()); Assert.Equal(invoice.InvoiceCode, value.GetProperty("invoiceNumber").GetString());
        Assert.Equal("CANCELLED", value.GetProperty("status").GetString());
        Assert.Equal(InvoiceCancellationTests.TestClock.GetUtcNow(), value.GetProperty("cancelledAt").GetDateTimeOffset());
        Assert.Equal(InvoiceCancellationTests.TestClock.GetUtcNow(), value.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(InvoiceCancellationTests.ValidRequest().CancellationReason.Trim(), value.GetProperty("cancellationReason").GetString());
        Assert.Equal("CREATE_ANOTHER", Assert.Single(value.GetProperty("availableActions").EnumerateArray()).GetString());
        var repeated = await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 409, "INVALID_INVOICE_STATUS");
        Assert.Equal("Invoice is already cancelled", repeated.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyActiveManagerCanCancel_IncludingForgedRole(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, _) = await InvoiceCancellationTests.SeedAsync(db);
        var employee = actor.EmployeeAccount!; employee.Role = role; await db.SaveChangesAsync();
        await using var app = await StartAsync(db); using var client = Client(app, actor.AccountId, JwtServices.GetRoleName(role));
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/cancel";
        Assert.Equal(expected, (int)(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest())).StatusCode);
        if (role != EmployeeRole.LOGISTICS_MANAGER)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
            await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 403, "FORBIDDEN");
            InvoiceCancellationTests.AssertNoEffects(db);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, customer.AccountId, "Customer"));
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 403, "FORBIDDEN");
        client.DefaultRequestHeaders.Authorization = null;
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 401, "UNAUTHORIZED");
        employee.IsActive = false; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
        await Error(await client.PostAsJsonAsync(url, InvoiceCancellationTests.ValidRequest()), 401, "UNAUTHORIZED");
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-tests", ["JwtOptions:Audience"] = "invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration); builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<TimeProvider>(InvoiceCancellationTests.TestClock);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(x => x.Value!.Errors.Count > 0).SelectMany(x => x.Value!.Errors.Select(e => new {
                    field = string.IsNullOrEmpty(x.Key) ? "$" : char.ToLowerInvariant(x.Key[0]) + x.Key[1..], message = e.ErrorMessage })),
                "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IService, Pegaxus.Service.InvoiceService.Service>();
        builder.Services.AddScoped<Pegaxus.Service.PaymentGatewayService.IService, InvoiceOnlinePaymentTests.Gateway>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.BookingListRequest>, BookingListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.IssueRequest>, IssueRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.RecordPaymentRequest>, RecordPaymentRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CancelRequest>, CancelRequestValidator>();
        builder.Services.AddScoped<Pegaxus.Service.NotificationService.IService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
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
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString()); return body;
    }
}
