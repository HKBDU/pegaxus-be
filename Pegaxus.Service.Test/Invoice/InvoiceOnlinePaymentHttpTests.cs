using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;

using Invoices = Pegaxus.Service.InvoiceService;

namespace Pegaxus.Service.Test;

public class InvoiceOnlinePaymentHttpTests
{
    private const string Secret = "iv11-tests-only-secret-key-with-more-than-32-characters";

    [Fact]
    public async Task Http_ContractOwnershipRolesForgedClaimsAndGatewayFailure()
    {
        await using var db = TestSupport.CreateDb();
        var (customer, manager, invoice) = await InvoiceOnlinePaymentTests.SeedAsync(db);
        var stranger = await TestSupport.AddCustomerAsync(db); var gateway = new InvoiceOnlinePaymentTests.Gateway();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(InvoicesController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "iv11-test",
            ["JwtOptions:Audience"] = "iv11-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Invoices.IInvoiceService>(_ => InvoiceOnlinePaymentTests.CreateService(db, gateway));
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var path = $"/api/v1/invoices/{invoice.InvoiceId}/online-payments";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null)).StatusCode);
        foreach (var role in Enum.GetValues<EmployeeRole>())
        {
            manager.EmployeeAccount!.Role = role; await db.SaveChangesAsync();
            SignIn(client, manager.AccountId, JwtServices.GetRoleName(role));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path, null)).StatusCode);
        }
        SignIn(client, manager.AccountId, "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(path, null)).StatusCode);
        SignIn(client, stranger.Account.AccountId, "Customer");
        var foreign = await client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        SignIn(client, customer.AccountId, "Customer");
        invoice.IssuedAt = null; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path, null)).StatusCode);
        invoice.IssuedAt = InvoiceOnlinePaymentTests.Now.AddDays(-5); await db.SaveChangesAsync();
        var created = await client.PostAsJsonAsync(path, new { amount = 1, currency = "USD", receiptCode = "attacker" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var envelope = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(envelope.GetProperty("isSuccess").GetBoolean()); Assert.Equal(201, envelope.GetProperty("code").GetInt32());
        Assert.Equal("Online payment initiated successfully", envelope.GetProperty("message").GetString());
        var value = envelope.GetProperty("value"); var receipt = value.GetProperty("receipt");
        Assert.Equal(gateway.PaymentUrl, value.GetProperty("paymentUrl").GetString());
        Assert.Equal(invoice.TotalAmount, receipt.GetProperty("amount").GetDecimal()); Assert.Equal("VND", receipt.GetProperty("currency").GetString());
        Assert.Equal("PENDING", receipt.GetProperty("status").GetString()); Assert.Equal("BANK_TRANSFER", receipt.GetProperty("paymentMethod").GetString());
        Assert.NotEqual(Guid.Empty, receipt.GetProperty("receiptId").GetGuid());
        Assert.Equal(gateway.ReceiptCode, receipt.GetProperty("receiptCode").GetString());
        var fields = value.GetProperty("checkoutFields");
        Assert.Equal(gateway.ReceiptCode, fields.GetProperty("order_invoice_number").GetString());
        Assert.Equal("151800000", fields.GetProperty("order_amount").GetString());
        Assert.Equal("VND", fields.GetProperty("currency").GetString());
        foreach (var field in new[] { "transactionReference", "paidAt", "confirmedAt", "note", "recordedBy" })
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty(field).ValueKind);
        var detailPath = $"/api/v1/invoices/{invoice.InvoiceId}";
        var details = (await client.GetFromJsonAsync<JsonElement>(detailPath)).GetProperty("value");
        Assert.Equal("ISSUED", details.GetProperty("status").GetString());
        Assert.Equal(receipt.GetProperty("receiptId").GetGuid(), Assert.Single(details.GetProperty("receipts").EnumerateArray()).GetProperty("receiptId").GetGuid());
        var again = await client.PostAsync(path, null); // a second click reuses the pending receipt: 200, same receipt
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var repeated = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, repeated.GetProperty("code").GetInt32());
        Assert.Equal(receipt.GetProperty("receiptId").GetGuid(), repeated.GetProperty("value").GetProperty("receipt").GetProperty("receiptId").GetGuid());
        Assert.False(repeated.GetProperty("value").TryGetProperty("reused", out _));
        Assert.Empty(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
        var cancelPath = detailPath + "/cancel";
        var paymentPath = detailPath + "/receipts";
        var payment = new Invoices.Request.RecordPaymentRequest
        { Amount = (long)invoice.TotalAmount, PaymentMethod = "BANK_TRANSFER" };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(cancelPath, InvoiceCancellationTests.ValidRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(paymentPath, payment)).StatusCode);
        gateway.Failure = new HttpRequestException("Gateway unavailable");
        var failed = await client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal("PAYMENT_GATEWAY_ERROR", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        gateway.Failure = new Pegaxus.Service.Exceptions.Exceptions("Payment gateway is not configured", 503, "PAYMENT_GATEWAY_NOT_CONFIGURED");
        var unconfigured = await client.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.StatusCode);
        var unavailableBody = await unconfigured.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PAYMENT_GATEWAY_NOT_CONFIGURED", unavailableBody.GetProperty("errorCode").GetString());
        Assert.Equal("Payment gateway is not configured", unavailableBody.GetProperty("message").GetString());
        manager.EmployeeAccount!.Role = EmployeeRole.LOGISTICS_MANAGER; await db.SaveChangesAsync();
        SignIn(client, manager.AccountId, "LogisticsManager");
        var cancelled = await client.PostAsJsonAsync(cancelPath, InvoiceCancellationTests.ValidRequest());
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        Assert.Equal("PAYMENT_IN_PROGRESS", (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var paid = await client.PostAsJsonAsync(paymentPath, payment);
        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var confirmed = (await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("receipt");
        SignIn(client, customer.AccountId, "Customer");
        details = (await client.GetFromJsonAsync<JsonElement>(detailPath)).GetProperty("value");
        Assert.Equal("PAID", details.GetProperty("status").GetString());
        var visibleReceipt = Assert.Single(details.GetProperty("receipts").EnumerateArray(), x => x.GetProperty("status").GetString() == "CONFIRMED");
        Assert.Equal(confirmed.GetProperty("receiptId").GetGuid(), visibleReceipt.GetProperty("receiptId").GetGuid());
        Assert.Equal(manager.AccountId, visibleReceipt.GetProperty("recordedBy").GetProperty("accountId").GetGuid());
        var history = Assert.Single(await db.InvoiceStatusHistories.ToListAsync());
        Assert.Equal(InvoiceHistoryType.RECORD_PAYMENT, history.Type); Assert.Equal(manager.AccountId, history.ChangedByAccountId);
        var calls = gateway.Calls;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path, null)).StatusCode);
        Assert.Equal(calls, gateway.Calls);
        customer.Status = AccountStatus.SUSPENDED; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null)).StatusCode);
        await app.StopAsync();
    }

    private static void SignIn(HttpClient client, Guid accountId, string role)
    {
        var token = new JwtSecurityToken("iv11-test", "iv11-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
