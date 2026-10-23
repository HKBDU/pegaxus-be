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
using Pegaxus.API.Models;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;

namespace Pegaxus.Service.Test;

public class InvoiceListHttpTests
{
    [Fact]
    public async Task Http_ReturnsContractFieldsPaginationAndValidationErrors()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceListTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, f.Account.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/invoices");
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoices retrieved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(1, value.GetProperty("pageIndex").GetInt32());
        Assert.Equal(20, value.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, value.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, value.GetProperty("totalPages").GetInt32());
        var item = value.GetProperty("items")[0];
        Assert.Equal(new[] { "bookingCode", "bookingId", "createdAt", "currency", "customerName", "dueDate", "invoiceId",
            "invoiceNumber", "issuedAt", "paidAt", "status", "totalAmount", "transportPlanId", "type", "updatedAt" },
            item.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(f.Invoices[4].InvoiceId, item.GetProperty("invoiceId").GetGuid());
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), item.GetProperty("dueDate").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("transportPlanId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("issuedAt").ValueKind);
        Assert.Equal(TimeSpan.Zero, item.GetProperty("createdAt").GetDateTimeOffset().Offset);
        value = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/invoices?bookingId={f.Booking.BookingId}&transportPlanId={f.OldPlan.TransportPlanId}&type=FINAL&status=PAID&issuedFrom=2026-10-05&issuedTo=2026-10-05&dueFrom=2026-10-25&dueTo=2026-10-25&search=club%20alpha&sortBy=totalAmount:asc&pageSize=1"))
            .GetProperty("value");
        item = Assert.Single(value.GetProperty("items").EnumerateArray());
        Assert.Equal(f.Invoices[2].InvoiceId, item.GetProperty("invoiceId").GetGuid());
        Assert.Equal(f.LatestPlan.TransportPlanId, item.GetProperty("transportPlanId").GetGuid());
        Assert.Equal(1, value.GetProperty("pageSize").GetInt32());
        value = (await client.GetFromJsonAsync<JsonElement>("/api/v1/invoices?search=absent")).GetProperty("value");
        Assert.Empty(value.GetProperty("items").EnumerateArray());
        Assert.Equal(0, value.GetProperty("totalPages").GetInt32());
        foreach (var (query, field) in new[] { ("pageIndex=0", "pageIndex"), ("pageSize=101", "pageSize"),
                     ("pageIndex=abc", "pageIndex"), ("type=unknown", "type"), ("status=PARTIALLY_PAID", "status"),
                     ("bookingId=invalid", "bookingId"), ("transportPlanId=invalid", "transportPlanId"),
                     ("issuedFrom=invalid", "issuedFrom"), ("dueTo=invalid", "dueTo"),
                     ("issuedFrom=2026-10-06&issuedTo=2026-10-05", "issuedTo"), ("sortBy=invalid", "sortBy") })
        {
            var error = await Error(await client.GetAsync("/api/v1/invoices?" + query), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), entry => entry.GetProperty("field").GetString() == field);
        }
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyManagerRoleCanListInvoices(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceListTests.Fixture.CreateAsync(db);
        f.Employee.Role = role;
        await db.SaveChangesAsync();
        await using var host = await StartAsync(db);
        using var client = Client(host, f.Account.AccountId, JwtServices.GetRoleName(role));
        Assert.Equal(expected, (int)(await client.GetAsync("/api/v1/invoices")).StatusCode);
    }

    [Fact]
    public async Task Http_EnforcesCustomerMessageLiveRoleAndInactiveAccounts()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceListTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, f.Booking.Customer.AccountId, JwtServices.GetRoleName(AccountType.CUSTOMER));
        foreach (var path in new[] { "/api/v1/invoices", "/API/V1/INVOICES/" })
        {
            var error = await Error(await client.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
            Assert.Equal("Customer cannot access the invoice list", error.GetProperty("message").GetString());
        }
        var generic = await Error(await client.GetAsync("/api/v1/vehicles"), HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal("You do not have permission to perform this action", generic.GetProperty("message").GetString());
        using var manager = Client(host, f.Account.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        f.Employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync("/api/v1/invoices"), HttpStatusCode.Forbidden, "FORBIDDEN");
        f.Employee.IsActive = false;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync("/api/v1/invoices"), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        manager.DefaultRequestHeaders.Authorization = null;
        await Error(await manager.GetAsync("/api/v1/invoices"), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    internal static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "invoice-tests", ["JwtOptions:Audience"] = "invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(entry => entry.Value!.Errors.Count > 0).SelectMany(entry => entry.Value!.Errors.Select(error => new {
                    field = string.IsNullOrEmpty(entry.Key) ? "$" : char.ToLowerInvariant(entry.Key[0]) + entry.Key[1..], message = error.ErrorMessage })),
                "The request data is invalid", traceId: context.HttpContext.TraceIdentifier)));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IInvoiceService, Pegaxus.Service.InvoiceService.Service>();
        builder.Services.AddScoped<Pegaxus.Service.PaymentGatewayService.IPaymentGatewayService, InvoiceOnlinePaymentTests.Gateway>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.BookingListRequest>, BookingListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.IssueRequest>, IssueRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.RecordPaymentRequest>, RecordPaymentRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CancelRequest>, CancelRequestValidator>();
        builder.Services.AddScoped<NotificationService.INotificationService, NotificationService.Service>();
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
