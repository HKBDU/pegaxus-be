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

public class InvoicePaymentHttpTests
{
    [Fact]
    public async Task Http_ReceiptInvoiceEffectsAndErrors_MatchContract()
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(db, InvoiceType.FINAL);
        await using var app = await StartAsync(db);
        using var client = Client(app, actor.AccountId, "LogisticsManager");
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/receipts";
        var partial = InvoicePaymentTests.ValidRequest(); partial.Amount = 119;
        await Error(await client.PostAsJsonAsync(url, partial), 422, "PARTIAL_PAYMENT_NOT_SUPPORTED");
        partial = InvoicePaymentTests.ValidRequest(); partial.Currency = "VND";
        await Error(await client.PostAsJsonAsync(url, partial), 422, "CURRENCY_MISMATCH");
        await Error(await client.PostAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/receipts", InvoicePaymentTests.ValidRequest()), 404, "INVOICE_NOT_FOUND");
        invoice.Type = InvoiceType.QUOTATION; invoice.Status = InvoiceStatus.DRAFT; await db.SaveChangesAsync();
        await Error(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest()), 422, "UNPAYABLE_INVOICE_TYPE");
        invoice.Type = InvoiceType.FINAL; invoice.Status = InvoiceStatus.OVERDUE; await db.SaveChangesAsync();
        var request = InvoicePaymentTests.ValidRequest(); request.PaidAt = InvoicePaymentTests.TestClock.GetUtcNow().AddMinutes(-40);
        var response = await client.PostAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(201, body.GetProperty("code").GetInt32());
        Assert.Equal("Payment recorded successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value"); var receipt = value.GetProperty("receipt");
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("receiptId").GetGuid());
        Assert.Matches("^RCP-20261110-[0-9]{4}$", receipt.GetProperty("receiptCode").GetString());
        Assert.Equal(120, receipt.GetProperty("amount").GetDecimal()); Assert.Equal("EUR", receipt.GetProperty("currency").GetString());
        Assert.Equal("BANK_TRANSFER", receipt.GetProperty("paymentMethod").GetString()); Assert.Equal("CONFIRMED", receipt.GetProperty("status").GetString());
        Assert.Equal("FT26314123456", receipt.GetProperty("transactionReference").GetString()); Assert.Equal("Matched statement", receipt.GetProperty("note").GetString());
        Assert.Equal(request.PaidAt, receipt.GetProperty("paidAt").GetDateTimeOffset());
        Assert.Equal(InvoicePaymentTests.TestClock.GetUtcNow(), receipt.GetProperty("confirmedAt").GetDateTimeOffset());
        Assert.Equal(actor.AccountId, receipt.GetProperty("recordedBy").GetProperty("accountId").GetGuid());
        Assert.Equal("Test Manager", receipt.GetProperty("recordedBy").GetProperty("fullName").GetString());
        var paid = value.GetProperty("invoice");
        Assert.Equal(invoice.InvoiceId, paid.GetProperty("invoiceId").GetGuid()); Assert.Equal(invoice.InvoiceCode, paid.GetProperty("invoiceNumber").GetString());
        Assert.Equal("FINAL", paid.GetProperty("type").GetString()); Assert.Equal("PAID", paid.GetProperty("status").GetString());
        Assert.Equal(InvoicePaymentTests.TestClock.GetUtcNow(), paid.GetProperty("paidAt").GetDateTimeOffset());
        Assert.Equal(InvoicePaymentTests.TestClock.GetUtcNow(), paid.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal("CREATE_ANOTHER", Assert.Single(paid.GetProperty("availableActions").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("effects").GetProperty("transportPlanStatus").ValueKind);
        Assert.True(value.GetProperty("effects").GetProperty("creditNoteUnlocked").GetBoolean());
        var repeat = await Error(await client.PostAsJsonAsync(url, request), 409, "INVALID_INVOICE_STATUS");
        Assert.Equal("Cannot pay an invoice in status PAID", repeat.GetProperty("message").GetString());
        await Error(await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.InvoiceId}/cancel",
            InvoiceCancellationTests.ValidRequest()), 422, "INVOICE_HAS_PAYMENT");
        var details = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{invoice.InvoiceId}")).GetProperty("value");
        Assert.Equal("PAID", details.GetProperty("status").GetString());
        Assert.Equal(receipt.GetRawText(), Assert.Single(details.GetProperty("receipts").EnumerateArray()).GetRawText());
        Assert.Equal("CREATE_ANOTHER", Assert.Single(details.GetProperty("availableActions").EnumerateArray()).GetString());
        using var customerClient = Client(app, customer.AccountId, "Customer");
        await Error(await customerClient.PostAsJsonAsync(url, request), 403, "FORBIDDEN");
        var customerDetails = (await customerClient.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{invoice.InvoiceId}")).GetProperty("value");
        Assert.Equal("PAID", customerDetails.GetProperty("status").GetString());
        Assert.Equal(receipt.GetRawText(), Assert.Single(customerDetails.GetProperty("receipts").EnumerateArray()).GetRawText());
        Assert.Empty(customerDetails.GetProperty("availableActions").EnumerateArray());
        var list = (await customerClient.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{invoice.BookingId}/invoices")).GetProperty("value");
        Assert.Equal("PAID", Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        var history = (await customerClient.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{invoice.InvoiceId}/status-history")).GetProperty("value");
        var paymentEvent = Assert.Single(history.EnumerateArray());
        Assert.Equal("RECORD_PAYMENT", paymentEvent.GetProperty("type").GetString());
        Assert.Equal(actor.AccountId, paymentEvent.GetProperty("changedBy").GetProperty("accountId").GetGuid());
    }

    [Theory]
    [InlineData("missingAmount", "amount")]
    [InlineData("zeroAmount", "amount")]
    [InlineData("negativeAmount", "amount")]
    [InlineData("precision", "amount")]
    [InlineData("overflow", "amount")]
    [InlineData("missingCurrency", "currency")]
    [InlineData("unsupportedCurrency", "currency")]
    [InlineData("missingMethod", "paymentMethod")]
    [InlineData("unknownMethod", "paymentMethod")]
    [InlineData("numericMethod", "paymentMethod")]
    [InlineData("referenceLength", "transactionReference")]
    [InlineData("futurePayment", "paidAt")]
    public async Task Http_Validation_ReturnsCamelCaseErrorsAndNoWrites(string scenario, string field)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, _, invoice, _) = await InvoicePaymentTests.SeedAsync(db);
        await using var app = await StartAsync(db); using var client = Client(app, actor.AccountId, "LogisticsManager");
        var request = InvoicePaymentTests.ValidRequest();
        switch (scenario)
        {
            case "missingAmount": request.Amount = null; break;
            case "zeroAmount": request.Amount = 0; break;
            case "negativeAmount": request.Amount = -1; break;
            case "precision": request.Amount = 120.001m; break;
            case "overflow": request.Amount = 10000000000000000m; break;
            case "missingCurrency": request.Currency = ""; break;
            case "unsupportedCurrency": request.Currency = "USD"; break;
            case "missingMethod": request.PaymentMethod = ""; break;
            case "unknownMethod": request.PaymentMethod = "CARD"; break;
            case "numericMethod": request.PaymentMethod = "1"; break;
            case "referenceLength": request.TransactionReference = new string('x', 151); break;
            case "futurePayment": request.PaidAt = InvoicePaymentTests.TestClock.GetUtcNow().AddTicks(1); break;
        }
        var error = await Error(await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.InvoiceId}/receipts", request), 400, "VALIDATION_ERROR");
        Assert.Contains(error.GetProperty("errors").EnumerateArray(), x => x.GetProperty("field").GetString() == field);
        InvoicePaymentTests.AssertNoEffects(db);
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 201)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyActiveManagerCanRecordPayment_IncludingForgedRole(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var (actor, customer, invoice, _) = await InvoicePaymentTests.SeedAsync(db);
        var employee = actor.EmployeeAccount!; employee.Role = role; await db.SaveChangesAsync();
        await using var app = await StartAsync(db); using var client = Client(app, actor.AccountId, JwtServices.GetRoleName(role));
        var url = $"/api/v1/invoices/{invoice.InvoiceId}/receipts";
        Assert.Equal(expected, (int)(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest())).StatusCode);
        if (role != EmployeeRole.LOGISTICS_MANAGER)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
            await Error(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest()), 403, "FORBIDDEN");
            InvoicePaymentTests.AssertNoEffects(db);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, customer.AccountId, "Customer"));
        await Error(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest()), 403, "FORBIDDEN");
        client.DefaultRequestHeaders.Authorization = null;
        await Error(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest()), 401, "UNAUTHORIZED");
        employee.IsActive = false; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(app, actor.AccountId, "LogisticsManager"));
        await Error(await client.PostAsJsonAsync(url, InvoicePaymentTests.ValidRequest()), 401, "UNAUTHORIZED");
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-tests", ["JwtOptions:Audience"] = "invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration); builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<TimeProvider>(InvoicePaymentTests.TestClock);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(x => x.Value!.Errors.Count > 0).SelectMany(x => x.Value!.Errors.Select(e => new {
                    field = string.IsNullOrEmpty(x.Key) ? "$" : char.ToLowerInvariant(x.Key[0]) + x.Key[1..], message = e.ErrorMessage })),
                "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IService, Pegaxus.Service.InvoiceService.Service>();
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
