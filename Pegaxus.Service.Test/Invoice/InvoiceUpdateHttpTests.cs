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
using Microsoft.EntityFrameworkCore;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.InvoiceService;
using Pegaxus.Service.InvoiceService.Validator;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.Models;
using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class InvoiceUpdateHttpTests
{
    [Fact]
    public async Task ActualJwtHttp_UpdateEnforcesRolesValidationVersionAndReturnsFullInvoice()
    {
        await using var db = TestSupport.CreateDb();
        var f = await InvoiceUpdateTests.Fixture.CreateAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(InvoicesController).Assembly.GetName().Name, EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = "iv3-test-secret-with-more-than-thirty-two-characters",
            ["JwtOptions:Issuer"] = "iv3-test", ["JwtOptions:Audience"] = "iv3-test",
            ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(InvoiceUpdateTests.Fixture.Clock);
        builder.Services.AddScoped<IService, InvoiceService.Service>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.SuggestionRequest>, SuggestionRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly)
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
        var path = $"/api/v1/invoices/{f.Invoice.InvoiceId}";
        var request = f.Request();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, request)).StatusCode);
        foreach (var role in new[] { "Customer", "TransportSpecialist", "FleetRouteCoordinator", "Driver", "Escort" })
        {
            SignIn(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, request)).StatusCode);
        }
        SignIn(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        request.Items = [InvoiceUpdateTests.Fixture.Item(0, 1)];
        var invalid = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("errorCode").GetString());
        Assert.Equal("items[0].quantity", error.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path, new { lastUpdatedAt = "not-a-timestamp", currency = "VND", dueDate = "2026-10-12", items = Array.Empty<object>() })).StatusCode);
        request.Items[0].Quantity = 1;
        request.Items[0].InvoiceItemId = f.Invoice.InvoiceItems.First().InvoiceItemId;
        request.Items[0].HorseId = f.Horse.HorseId;
        var updated = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Invoice updated successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(f.Invoice.InvoiceId, value.GetProperty("invoiceId").GetGuid());
        Assert.Equal(f.Invoice.InvoiceCode, value.GetProperty("invoiceNumber").GetString());
        Assert.Equal("DRAFT", value.GetProperty("status").GetString());
        Assert.False(value.GetProperty("isPaymentRequired").GetBoolean());
        Assert.Equal(1, value.GetProperty("items").GetArrayLength());
        Assert.Equal(f.Horse.HorseId, value.GetProperty("items")[0].GetProperty("horse").GetProperty("horseId").GetGuid());
        Assert.Equal(1.2m, value.GetProperty("summary").GetProperty("grandTotal").GetDecimal());
        Assert.Equal(InvoiceUpdateTests.Fixture.Clock.GetUtcNow(), value.GetProperty("updatedAt").GetDateTimeOffset());
        foreach (var field in new[] { "payment", "issuedAt", "paidAt", "cancelledAt", "cancellationReason" })
            Assert.Equal(JsonValueKind.Null, value.GetProperty(field).ValueKind);
        Assert.Equal(4, value.GetProperty("availableActions").GetArrayLength());
        Assert.Equal(0, value.GetProperty("receipts").GetArrayLength());
        var stale = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("INVOICE_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var invoice = await db.Invoices.SingleAsync();
        invoice.Status = InvoiceStatus.ISSUED; await db.SaveChangesAsync();
        request.LastUpdatedAt = value.GetProperty("updatedAt").GetDateTimeOffset();
        var locked = await client.PutAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("INVOICE_LOCKED", (await locked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        f.Employee.Role = EmployeeRole.DRIVER; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, request)).StatusCode);
        f.Employee.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, request)).StatusCode);
        await app.StopAsync();

        void SignIn(string role)
        {
            var token = new JwtServices(builder.Configuration).GenerateAccessToken(
                [new Claim(ClaimTypes.NameIdentifier, f.Account.AccountId.ToString()), new Claim(ClaimTypes.Role, role)]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
