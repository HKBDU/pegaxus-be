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

public class BookingInvoiceListHttpTests
{
    [Fact]
    public async Task Http_ReturnsContractFieldsAndKeepsCustomerVisibilityAndBookingOwnership()
    {
        await using var db = TestSupport.CreateDb();
        var f = await BookingInvoiceListTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var customer = Client(host, f.Booking.Customer.AccountId, "Customer");
        var path = $"/api/v1/bookings/{f.Booking.BookingId}/invoices";
        var body = await customer.GetFromJsonAsync<JsonElement>(path);
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoices retrieved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(1, value.GetProperty("pageIndex").GetInt32());
        Assert.Equal(20, value.GetProperty("pageSize").GetInt32());
        Assert.Equal(4, value.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, value.GetProperty("totalPages").GetInt32());
        var item = value.GetProperty("items")[0];
        Assert.Equal(new[] { "bookingCode", "bookingId", "createdAt", "currency", "customerName", "dueDate", "invoiceId",
            "invoiceNumber", "issuedAt", "paidAt", "status", "totalAmount", "transportPlanId", "type", "updatedAt" },
            item.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(f.Invoices[4].InvoiceId, item.GetProperty("invoiceId").GetGuid());
        Assert.Equal("CANCELLED", item.GetProperty("status").GetString());
        Assert.Equal("2026-10-10", item.GetProperty("dueDate").GetString());
        Assert.Equal(TimeSpan.Zero, item.GetProperty("issuedAt").GetDateTimeOffset().Offset);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("paidAt").ValueKind);
        Assert.Equal(f.LatestPlan.TransportPlanId, item.GetProperty("transportPlanId").GetGuid());
        value = (await customer.GetFromJsonAsync<JsonElement>(path + "?type=FINAL&status=PAID&sortBy=totalAmount:asc&pageSize=1")).GetProperty("value");
        Assert.Equal(f.Invoices[2].InvoiceId, Assert.Single(value.GetProperty("items").EnumerateArray()).GetProperty("invoiceId").GetGuid());
        Assert.Equal(1, value.GetProperty("pageSize").GetInt32());
        value = (await customer.GetFromJsonAsync<JsonElement>(path + $"?bookingId={f.OtherBooking.BookingId}&status=DRAFT")).GetProperty("value");
        Assert.Empty(value.GetProperty("items").EnumerateArray());
        Assert.Equal(0, value.GetProperty("totalItems").GetInt32());
        Assert.Equal(0, value.GetProperty("totalPages").GetInt32());
        value = (await customer.GetFromJsonAsync<JsonElement>(path + "?pageIndex=3&pageSize=2")).GetProperty("value");
        Assert.Empty(value.GetProperty("items").EnumerateArray());
        Assert.Equal(4, value.GetProperty("totalItems").GetInt32());
        value = (await customer.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{f.EmptyBooking.BookingId}/invoices")).GetProperty("value");
        Assert.Empty(value.GetProperty("items").EnumerateArray());
        foreach (var id in new[] { f.OtherBooking.BookingId, Guid.NewGuid() })
        {
            var error = await Error(await customer.GetAsync($"/api/v1/bookings/{id}/invoices"), HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");
            Assert.Equal("Booking not found", error.GetProperty("message").GetString());
        }
        using var manager = Client(host, f.Account.AccountId, "LogisticsManager");
        value = (await manager.GetFromJsonAsync<JsonElement>(path)).GetProperty("value");
        Assert.Equal(7, value.GetProperty("totalItems").GetInt32());
        value = (await manager.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{f.OtherBooking.BookingId}/invoices")).GetProperty("value");
        Assert.Equal(JsonValueKind.Null, Assert.Single(value.GetProperty("items").EnumerateArray()).GetProperty("transportPlanId").ValueKind);
        foreach (var (query, field) in new[] { ("pageIndex=0", "pageIndex"), ("pageSize=101", "pageSize"),
                     ("pageIndex=abc", "pageIndex"), ("pageIndex=2147483647", "pageIndex"),
                     ("type=unknown", "type"), ("status=PARTIALLY_PAID", "status"), ("sortBy=invalid", "sortBy") })
        {
            var error = await Error(await manager.GetAsync(path + "?" + query), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), entry => entry.GetProperty("field").GetString() == field);
        }
        foreach (var id in new[] { "invalid", Guid.Empty.ToString() })
        {
            var error = await Error(await manager.GetAsync($"/api/v1/bookings/{id}/invoices"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), entry => entry.GetProperty("field").GetString() == "bookingId");
        }
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_OnlyManagerEmployeeRoleCanListBookingInvoices(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var f = await BookingInvoiceListTests.Fixture.CreateAsync(db);
        f.Employee.Role = role;
        await db.SaveChangesAsync();
        await using var host = await StartAsync(db);
        using var client = Client(host, f.Account.AccountId, JwtServices.GetRoleName(role));
        Assert.Equal(expected, (int)(await client.GetAsync($"/api/v1/bookings/{f.Booking.BookingId}/invoices")).StatusCode);
        if (expected == 403)
            await Error(await client.GetAsync($"/api/v1/bookings/{f.Booking.BookingId}/invoices?pageIndex=invalid"), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Http_RejectsMissingTokensStaleManagerRoleAndInactiveProfiles()
    {
        await using var db = TestSupport.CreateDb();
        var f = await BookingInvoiceListTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db);
        using var manager = Client(host, f.Account.AccountId, "LogisticsManager");
        var path = $"/api/v1/bookings/{f.Booking.BookingId}/invoices";
        f.Employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
        f.Employee.IsActive = false;
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
            ["JwtOptions:Issuer"] = "booking-invoice-tests", ["JwtOptions:Audience"] = "booking-invoice-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddControllers().AddApplicationPart(typeof(BookingInvoicesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(entry => entry.Value!.Errors.Count > 0).SelectMany(entry => entry.Value!.Errors.Select(error => new {
                    field = string.IsNullOrEmpty(entry.Key) ? "$" : char.ToLowerInvariant(entry.Key[0]) + entry.Key[1..], message = error.ErrorMessage })),
                "The request data is invalid", traceId: context.HttpContext.TraceIdentifier)));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.InvoiceService.IService, Pegaxus.Service.InvoiceService.Service>();
        builder.Services.AddScoped<IValidator<Request.BookingListRequest>, BookingListRequestValidator>();
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
