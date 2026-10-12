using System.Net;
using System.Net.Http.Headers;
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
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.Models;

namespace Pegaxus.Service.Test;

public class InvoiceDetailHttpTests
{
    [Fact]
    public async Task Http_SerializesTheFullContractAndProtectsOwnedAndForeignDrafts()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceDetailTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var customer = Client(host, f.Booking.Customer.AccountId, "Customer");
        var path = $"/api/v1/invoices/{f.Invoice.InvoiceId}";
        var body = await customer.GetFromJsonAsync<JsonElement>(path);
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice retrieved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(new[] { "availableActions", "booking", "cancellationReason", "cancelledAt", "contactPerson", "createdAt",
            "currency", "customer", "dueDate", "invoiceDate", "invoiceId", "invoiceNumber", "isPaymentRequired", "issuedAt",
            "items", "note", "paidAt", "payment", "receipts", "status", "summary", "transport", "transportPlan", "type", "updatedAt" },
            value.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("2026-10-05", value.GetProperty("invoiceDate").GetString());
        Assert.Equal("2026-10-15", value.GetProperty("dueDate").GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("payment").ValueKind);
        Assert.Equal(TimeSpan.Zero, value.GetProperty("updatedAt").GetDateTimeOffset().Offset);
        Assert.Equal("Club Alpha", value.GetProperty("customer").GetProperty("name").GetString());
        Assert.Equal(f.Creator.FullName, value.GetProperty("contactPerson").GetProperty("name").GetString());
        Assert.Equal(new[] { "description", "emergencyCostRequestId", "horse", "invoiceItemId", "isDepositDeduction", "itemDate", "itemType", "note", "price", "quantity", "total" },
            value.GetProperty("items")[1].EnumerateObject().Select(property => property.Name).Order());
        Assert.True(value.GetProperty("items")[1].GetProperty("isDepositDeduction").GetBoolean());
        Assert.Equal(-30, value.GetProperty("items")[1].GetProperty("price").GetDecimal());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("items")[1].GetProperty("itemDate").ValueKind);
        Assert.Equal(-40, value.GetProperty("summary").GetProperty("depositDeduction").GetDecimal());
        Assert.Equal(20, value.GetProperty("summary").GetProperty("taxRatePercent").GetDecimal());
        Assert.Equal(86, value.GetProperty("summary").GetProperty("grandTotal").GetDecimal());
        var receipts = value.GetProperty("receipts");
        Assert.Equal(3, receipts.GetArrayLength());
        Assert.Equal(new[] { "amount", "confirmedAt", "currency", "note", "paidAt", "paymentMethod", "receiptCode", "receiptId", "recordedBy", "status", "transactionReference" },
            receipts[2].EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("PENDING", receipts[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, receipts[0].GetProperty("paidAt").ValueKind);
        Assert.Equal(f.Recorder.AccountId, receipts[2].GetProperty("recordedBy").GetProperty("accountId").GetGuid());
        Assert.Equal(f.LatestPlan.TransportPlanId, value.GetProperty("transportPlan").GetProperty("transportPlanId").GetGuid());
        Assert.Empty(value.GetProperty("availableActions").EnumerateArray());
        foreach (var id in new[] { Guid.NewGuid(), f.OtherInvoice.InvoiceId })
        {
            var error = await Error(await customer.GetAsync($"/api/v1/invoices/{id}"), HttpStatusCode.NotFound, "INVOICE_NOT_FOUND");
            Assert.Equal("Invoice not found", error.GetProperty("message").GetString());
        }
        using var manager = Client(host, f.Account.AccountId, "LogisticsManager");
        var draft = (await manager.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{f.OtherInvoice.InvoiceId}")).GetProperty("value");
        Assert.False(draft.GetProperty("isPaymentRequired").GetBoolean());
        Assert.Empty(draft.GetProperty("items").EnumerateArray());
        Assert.Empty(draft.GetProperty("receipts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("transport").ValueKind);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("transportPlan").ValueKind);
        Assert.Equal(new[] { "EDIT", "ISSUE", "CANCEL", "CREATE_ANOTHER" }, draft.GetProperty("availableActions").EnumerateArray().Select(item => item.GetString()));
        foreach (var status in new[] { InvoiceStatus.DRAFT, InvoiceStatus.CANCELLED })
        {
            f.Invoice.Status = status; f.Invoice.IssuedAt = null;
            await db.SaveChangesAsync();
            var error = await Error(await customer.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
            Assert.Equal("Customer cannot view draft invoices", error.GetProperty("message").GetString());
        }
        f.Invoice.IssuedAt = InvoiceDetailTests.Fixture.Day(5);
        await db.SaveChangesAsync();
        Assert.Equal("CANCELLED", (await customer.GetFromJsonAsync<JsonElement>(path)).GetProperty("value").GetProperty("status").GetString());
        foreach (var id in new[] { "invalid", Guid.Empty.ToString() })
        {
            var error = await Error(await manager.GetAsync("/api/v1/invoices/" + id), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), item => item.GetProperty("field").GetString() == "invoiceId");
        }
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyManagerEmployeeRoleMayViewInvoices(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceDetailTests.Fixture.CreateAsync(db);
        f.Creator.Role = role;
        await db.SaveChangesAsync();
        await using var host = await StartAsync(db);
        using var client = Client(host, f.Account.AccountId, JwtServices.GetRoleName(role));
        Assert.Equal(expected, (int)(await client.GetAsync($"/api/v1/invoices/{f.Invoice.InvoiceId}")).StatusCode);
        if (expected == 403) await Error(await client.GetAsync("/api/v1/invoices/invalid"), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Http_RejectsMissingTokensStaleManagerRoleAndInactiveProfiles()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceDetailTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var manager = Client(host, f.Account.AccountId, "LogisticsManager");
        var path = $"/api/v1/invoices/{f.Invoice.InvoiceId}";
        f.Creator.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
        f.Creator.IsActive = false;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync(path), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        using var customer = Client(host, f.Booking.Customer.AccountId, "Customer");
        f.Booking.Customer.IsDeleted = true;
        await db.SaveChangesAsync();
        await Error(await customer.GetAsync(path), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        manager.DefaultRequestHeaders.Authorization = null;
        await Error(await manager.GetAsync(path), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    internal static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-detail-tests", ["JwtOptions:Audience"] = "invoice-detail-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(entry => entry.Value!.Errors.Count > 0).SelectMany(entry => entry.Value!.Errors.Select(error => new {
                    field = string.IsNullOrEmpty(entry.Key) ? "$" : char.ToLowerInvariant(entry.Key[0]) + entry.Key[1..], message = error.ErrorMessage })),
                "The request data is invalid", traceId: context.HttpContext.TraceIdentifier)));
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
        builder.Services.AddScoped<NotificationService.IService, NotificationService.Service>();
        builder.Services.AddSingleton<NotificationService.INotificationRealtimeService, NotificationService.NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return app;
    }

    internal static HttpClient Client(WebApplication host, Guid id, string role)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role)]));
        return client;
    }

    private static async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("isSuccess").GetBoolean());
            Assert.Equal(code, body.GetProperty("errorCode").GetString());
            return body;
        }
    }
}
