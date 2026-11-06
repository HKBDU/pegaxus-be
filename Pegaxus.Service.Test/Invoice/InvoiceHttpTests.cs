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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.API.Models;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class InvoiceHttpTests
{
    [Fact]
    public async Task SuggestionCreateUpdateListsDetailsIssueCancelAndHistory_ActualJwtHttpContractEnforcesRolesAndReturnsCompleteDraft()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceTests.Fixture.CreateAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BookingController).Assembly.GetName().Name, EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = "iv2-test-secret-with-more-than-thirty-two-characters",
            ["JwtOptions:Issuer"] = "iv2-test", ["JwtOptions:Audience"] = "iv2-test",
            ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(InvoiceTests.Fixture.Clock);
        builder.Services.AddScoped<IInvoiceService, InvoiceService.Service>();
        builder.Services.AddScoped<Pegaxus.Service.PaymentGatewayService.IPaymentGatewayService, InvoiceOnlinePaymentTests.Gateway>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.BookingListRequest>, BookingListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.IssueRequest>, IssueRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.RecordPaymentRequest>, RecordPaymentRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CancelRequest>, CancelRequestValidator>();
        builder.Services.AddScoped<NotificationService.INotificationService, NotificationService.Service>();
        builder.Services.AddSingleton<NotificationService.INotificationRealtimeService, NotificationService.NoopNotificationRealtimeService>();
        builder.Services.AddScoped<Pegaxus.Service.BookingService.IBookingService>(_ =>
            new Pegaxus.Service.BookingService.Service(db, TestSupport.CreateMailService(), TestSupport.CreateNotifications(db)));
        builder.Services.AddControllers().AddApplicationPart(typeof(BookingController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
                new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                    context.ModelState.Where(item => item.Value!.Errors.Count > 0)
                        .SelectMany(item => item.Value!.Errors.Select(error => new { field = item.Key, message = error.ErrorMessage })),
                    "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/v1/bookings/{f.Booking.BookingId}/invoices";
        var request = InvoiceTests.Fixture.Request();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, request)).StatusCode);
        foreach (var role in new[] { "Customer", "TransportSpecialist", "FleetRouteCoordinator", "Driver", "Escort" })
        {
            SignIn(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, request)).StatusCode);
        }
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var suggestion = await client.GetAsync(path + "/suggestion?type=SUPPLEMENTARY");
        Assert.Equal(HttpStatusCode.OK, suggestion.StatusCode);
        var preview = (await suggestion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.True(preview.GetProperty("isPaymentRequired").GetBoolean());
        Assert.Equal(0, preview.GetProperty("items").GetArrayLength());
        request.Items = [InvoiceTests.Fixture.Item(0, 5)];
        var invalid = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("errorCode").GetString());
        Assert.Equal("items[0].quantity", error.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { type = "QUOTATION", dueDate = "bad", items = Array.Empty<object>() })).StatusCode);
        request.Items[0].Quantity = 1;
        request.Items[0].HorseId = f.Horse.HorseId;
        var created = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(201, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice created successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal("DRAFT", value.GetProperty("status").GetString());
        Assert.True(value.GetProperty("isPaymentRequired").GetBoolean());
        Assert.Equal(request.DueDate, value.GetProperty("dueDate").GetDateTimeOffset());
        Assert.Equal(f.Horse.HorseId, value.GetProperty("items")[0].GetProperty("horse").GetProperty("horseId").GetGuid());
        Assert.Equal("6", value.GetProperty("summary").GetProperty("grandTotal").GetRawText());
        foreach (var field in new[] { "payment", "issuedAt", "paidAt", "cancelledAt", "cancellationReason" })
            Assert.Equal(JsonValueKind.Null, value.GetProperty(field).ValueKind);
        Assert.Equal(4, value.GetProperty("availableActions").GetArrayLength());
        Assert.Equal(0, value.GetProperty("receipts").GetArrayLength());
        var invoiceId = value.GetProperty("invoiceId").GetGuid();
        var item = request.Items[0];
        item.InvoiceItemId = value.GetProperty("items")[0].GetProperty("invoiceItemId").GetGuid();
        item.Quantity = 2;
        var updateRequest = new Request.UpdateRequest
        {
            LastUpdatedAt = value.GetProperty("updatedAt").GetDateTimeOffset(),
            DueDate = request.DueDate, Items = [item]
        };
        var updated = await client.PutAsJsonAsync($"/api/v1/invoices/{invoiceId}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var edited = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(invoiceId, edited.GetProperty("invoiceId").GetGuid());
        Assert.Equal(item.InvoiceItemId, edited.GetProperty("items")[0].GetProperty("invoiceItemId").GetGuid());
        Assert.Equal("12", edited.GetProperty("summary").GetProperty("grandTotal").GetRawText());
        Assert.True(edited.GetProperty("updatedAt").GetDateTimeOffset() > updateRequest.LastUpdatedAt);
        Assert.Single(await db.InvoiceStatusHistories.ToListAsync());
        var historyPath = $"/api/v1/invoices/{invoiceId}/status-history";
        var draftHistory = (await client.GetFromJsonAsync<JsonElement>(historyPath)).GetProperty("value");
        Assert.Equal("CREATE", Assert.Single(draftHistory.EnumerateArray()).GetProperty("type").GetString());
        var stale = await client.PutAsJsonAsync($"/api/v1/invoices/{invoiceId}", updateRequest);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("INVOICE_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var listed = await client.GetAsync($"/api/v1/invoices?bookingId={f.Booking.BookingId}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var list = (await listed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(1, list.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
        Assert.Equal(invoiceId, list.GetProperty("items")[0].GetProperty("invoiceId").GetGuid());
        Assert.Equal("12", list.GetProperty("items")[0].GetProperty("totalAmount").GetRawText());
        var bookingList = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, bookingList.StatusCode);
        var bookingInvoices = (await bookingList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(1, bookingInvoices.GetProperty("totalItems").GetInt32());
        Assert.Equal(invoiceId, bookingInvoices.GetProperty("items")[0].GetProperty("invoiceId").GetGuid());
        Assert.Equal("12", bookingInvoices.GetProperty("items")[0].GetProperty("totalAmount").GetRawText());
        var detailResponse = await client.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(invoiceId, detail.GetProperty("invoiceId").GetGuid());
        Assert.Equal("DRAFT", detail.GetProperty("status").GetString());
        Assert.Equal(item.InvoiceItemId, detail.GetProperty("items")[0].GetProperty("invoiceItemId").GetGuid());
        Assert.Equal("12", detail.GetProperty("summary").GetProperty("grandTotal").GetRawText());
        Assert.Equal(edited.GetProperty("updatedAt").GetDateTimeOffset(), detail.GetProperty("updatedAt").GetDateTimeOffset());
        SignIn("Customer", f.Booking.Customer.AccountId);
        var customerList = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, customerList.StatusCode);
        var customerInvoices = (await customerList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(0, customerInvoices.GetProperty("totalItems").GetInt32());
        Assert.Equal(0, customerInvoices.GetProperty("items").GetArrayLength());
        var customerDetail = await client.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.Forbidden, customerDetail.StatusCode);
        Assert.Equal("Customer cannot view draft invoices", (await customerDetail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(historyPath)).StatusCode);
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var duplicate = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("INVOICE_ALREADY_EXISTS", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var duplicateSuggestion = await client.GetAsync(path + "/suggestion?type=SUPPLEMENTARY");
        Assert.Equal(HttpStatusCode.Conflict, duplicateSuggestion.StatusCode);
        Assert.Equal("INVOICE_ALREADY_EXISTS", (await duplicateSuggestion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var issuedResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/issue",
            new Request.IssueRequest { LastUpdatedAt = edited.GetProperty("updatedAt").GetDateTimeOffset() });
        Assert.Equal(HttpStatusCode.OK, issuedResponse.StatusCode);
        var issued = (await issuedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(invoiceId, issued.GetProperty("invoiceId").GetGuid());
        Assert.Equal("ISSUED", issued.GetProperty("status").GetString());
        Assert.Equal("12", issued.GetProperty("grandTotal").GetRawText());
        Assert.Equal(2, await db.InvoiceStatusHistories.CountAsync());
        Assert.Single(await db.OutboxMessages.ToListAsync());
        SignIn("Customer", f.Booking.Customer.AccountId);
        var issuedDetails = await client.GetAsync($"/api/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, issuedDetails.StatusCode);
        Assert.Equal("ISSUED", (await issuedDetails.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("status").GetString());
        var issuedHistory = (await client.GetFromJsonAsync<JsonElement>(historyPath)).GetProperty("value");
        Assert.Equal("ISSUE", Assert.Single(issuedHistory.EnumerateArray()).GetProperty("type").GetString());
        var visibleInvoices = (await client.GetFromJsonAsync<JsonElement>(path)).GetProperty("value");
        Assert.Equal(1, visibleInvoices.GetProperty("totalItems").GetInt32());
        Assert.Equal(invoiceId, visibleInvoices.GetProperty("items")[0].GetProperty("invoiceId").GetGuid());
        var cancellation = InvoiceCancellationTests.ValidRequest();
        var cancelPath = $"/api/v1/invoices/{invoiceId}/cancel";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(cancelPath, cancellation)).StatusCode);
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var cancelResponse = await client.PostAsJsonAsync(cancelPath, cancellation);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("CANCELLED", cancelled.GetProperty("status").GetString());
        Assert.Equal(cancellation.CancellationReason.Trim(), cancelled.GetProperty("cancellationReason").GetString());
        Assert.Equal(3, await db.InvoiceStatusHistories.CountAsync());
        Assert.Equal(2, await db.OutboxMessages.CountAsync());
        SignIn("Customer", f.Booking.Customer.AccountId);
        var cancelledDetails = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/invoices/{invoiceId}")).GetProperty("value");
        Assert.Equal("CANCELLED", cancelledDetails.GetProperty("status").GetString());
        Assert.Equal(cancelled.GetProperty("cancelledAt").GetDateTimeOffset(), cancelledDetails.GetProperty("cancelledAt").GetDateTimeOffset());
        Assert.Empty(cancelledDetails.GetProperty("availableActions").EnumerateArray());
        var customerHistory = (await client.GetFromJsonAsync<JsonElement>(historyPath)).GetProperty("value");
        Assert.Equal(new[] { "CANCEL", "ISSUE" }, customerHistory.EnumerateArray()
            .Select(x => x.GetProperty("type").GetString()).OrderBy(x => x, StringComparer.Ordinal));
        var cancelEvent = Assert.Single(customerHistory.EnumerateArray(), x => x.GetProperty("type").GetString() == "CANCEL");
        Assert.Equal(cancellation.CancellationReason.Trim(), cancelEvent.GetProperty("reason").GetString());
        Assert.Equal(f.Account.AccountId, cancelEvent.GetProperty("changedBy").GetProperty("accountId").GetGuid());
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>(historyPath)).GetProperty("value").GetArrayLength());
        var replacement = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.NotEqual(invoiceId, (await replacement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("invoiceId").GetGuid());
        f.Employee.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, request)).StatusCode);
        f.Employee.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, request)).StatusCode);
        await app.StopAsync();

        void SignIn(string role, Guid? accountId = null)
        {
            var token = new JwtServices(builder.Configuration).GenerateAccessToken(
                [new Claim(ClaimTypes.NameIdentifier, (accountId ?? f.Account.AccountId).ToString()), new Claim(ClaimTypes.Role, role)]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
