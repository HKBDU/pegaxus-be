using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Test.HandoverTests;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public partial class HandoverHttpTests
{
    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200, 200)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403, 403)]
    [InlineData(EmployeeRole.DRIVER, 200, 403)]
    [InlineData(EmployeeRole.ESCORT, 200, 403)]
    public async Task Http_RoutesPoliciesAndStateTransitionsUseRealServiceAndEnvelopes(EmployeeRole role, int startStatus, int cancelStatus)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var actor = role switch { EmployeeRole.LOGISTICS_MANAGER => f.Manager, EmployeeRole.FLEET_ROUTE_COORDINATOR => f.Coordinator,
            EmployeeRole.DRIVER => f.Driver, EmployeeRole.ESCORT => f.Escort, _ => await PersonnelAssignmentTests.EmployeeAsync(db, role) };
        await using var host = await StartAsync(db, f); using var client = Client(host, actor.AccountId, role);
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/transports/{f.TransportId}/handover-records");
        Assert.True(list.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, list.GetProperty("code").GetInt32());
        Assert.Equal("Handover records retrieved successfully", list.GetProperty("message").GetString());
        Assert.Equal(0, list.GetProperty("value").GetProperty("pageIndex").GetInt32());
        Assert.Equal(3, list.GetProperty("value").GetProperty("items").GetArrayLength());
        var url = $"/api/v1/handover-records/{f.Pickup.HandoverRecordId}";
        var detail = await client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal("Handover record retrieved successfully", detail.GetProperty("message").GetString());
        using var started = await client.PostAsync(url + "/start", null); Assert.Equal(startStatus, (int)started.StatusCode);
        if (startStatus == 200)
        {
            detail = await started.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("IN_PROGRESS", detail.GetProperty("value").GetProperty("status").GetString());
            Assert.Equal(f.Clock.GetUtcNow(), detail.GetProperty("value").GetProperty("updatedAt").GetDateTimeOffset());
        }
        using var cancelled = await client.PostAsJsonAsync(url + "/cancel", new { lastUpdatedAt = detail.GetProperty("value").GetProperty("updatedAt").GetDateTimeOffset(), cancellationReason = "Primary road is closed" });
        Assert.Equal(cancelStatus, (int)cancelled.StatusCode);
        if (cancelStatus == 200)
        {
            var record = (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
            Assert.Equal("CANCELLED", record.GetProperty("status").GetString()); Assert.Equal(0, record.GetProperty("availableActions").GetArrayLength());
            using var terminal = await client.PostAsync(url + "/start", null);
            Assert.Equal("INVALID_HANDOVER_STATUS", (await ErrorAsync(terminal, HttpStatusCode.Conflict)).GetProperty("errorCode").GetString());
        }
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Http_CustomersCannotReadForeignOrInternalRecordsAndEmployeePhonesAreRedacted()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var (other, _) = await TestSupport.AddCustomerAsync(db);
        await using var host = await StartAsync(db, f); using var owner = Client(host, f.CustomerAccountId); using var stranger = Client(host, other.AccountId);
        var listUrl = $"/api/v1/transports/{f.TransportId}/handover-records"; var url = $"/api/v1/handover-records/{f.Pickup.HandoverRecordId}";
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>(listUrl)).GetProperty("value").GetProperty("totalItems").GetInt32());
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>(listUrl + "?handoverType=TRANSFER")).GetProperty("value").GetProperty("totalItems").GetInt32());
        var pickup = (await owner.GetFromJsonAsync<JsonElement>(url)).GetProperty("value");
        Assert.Equal(JsonValueKind.Null, pickup.GetProperty("to").GetProperty("contactPhone").ValueKind);
        Assert.Equal("customer-phone", pickup.GetProperty("from").GetProperty("contactPhone").GetString());
        Assert.Equal(JsonValueKind.Null, pickup.GetProperty("to").GetProperty("transportProviderId").ValueKind);
        Assert.Equal("Healthy horses", pickup.GetProperty("horseConditionNote").GetString());
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await ErrorAsync(await owner.GetAsync($"/api/v1/handover-records/{f.Transfer.HandoverRecordId}"), HttpStatusCode.NotFound)).GetProperty("errorCode").GetString());
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await ErrorAsync(await stranger.GetAsync(url), HttpStatusCode.NotFound)).GetProperty("errorCode").GetString());
        Assert.Equal("TRANSPORT_NOT_FOUND", (await ErrorAsync(await stranger.GetAsync(listUrl), HttpStatusCode.NotFound)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync(url + "/start", null)).StatusCode);
    }

    [Fact]
    public async Task Http_ValidationVersionsAndActualDatabaseRolesOverrideForgedClaims()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f); using var client = Client(host, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var url = $"/api/v1/handover-records/{f.Pickup.HandoverRecordId}";
        foreach (var query in new[] { "pageIndex=-1", "pageSize=0", "status=1", "handoverType=BAD", "sortBy=bad", "pageIndex=oops" })
            Assert.Equal("VALIDATION_ERROR", (await ErrorAsync(await client.GetAsync($"/api/v1/transports/{f.TransportId}/handover-records?{query}"), HttpStatusCode.BadRequest)).GetProperty("errorCode").GetString());
        foreach (var request in new object[] { new { cancellationReason = "Primary road is closed" }, new { lastUpdatedAt = f.Clock.GetUtcNow(), cancellationReason = "   123456789   " } })
            Assert.Equal("VALIDATION_ERROR", (await ErrorAsync(await client.PostAsJsonAsync(url + "/cancel", request), HttpStatusCode.BadRequest)).GetProperty("errorCode").GetString());
        var stale = f.Cancel(f.Pickup); stale.LastUpdatedAt = stale.LastUpdatedAt!.Value.AddMinutes(-1);
        Assert.Equal("HANDOVER_MODIFIED", (await ErrorAsync(await client.PostAsJsonAsync(url + "/cancel", stale), HttpStatusCode.Conflict)).GetProperty("errorCode").GetString());
        using var forged = Client(host, f.Driver.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        Assert.Equal("FORBIDDEN", (await ErrorAsync(await forged.PostAsJsonAsync(url + "/cancel", f.Cancel(f.Pickup)), HttpStatusCode.Forbidden)).GetProperty("errorCode").GetString());
        f.Driver.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync(url)).StatusCode);
        Assert.Equal(HandoverStatus.PLANNED, f.Pickup.Status); Assert.Empty(db.Notifications);
        Assert.Equal("HANDOVER_RECORD_NOT_FOUND", (await ErrorAsync(await client.PostAsync($"/api/v1/handover-records/{Guid.NewGuid()}/start", null), HttpStatusCode.NotFound)).GetProperty("errorCode").GetString());
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.False(body.GetProperty("isSuccess").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString())); return body;
        }
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, Fixture fixture)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "handover-tests", ["JwtOptions:Audience"] = "handover-tests" });
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<TimeProvider>(fixture.Clock);
        builder.Services.AddSingleton<Pegaxus.Service.MediaService.IMediaService>(fixture.Media);
        builder.Services.AddApiControllers(); builder.Services.AddControllers().AddApplicationPart(typeof(HandoverRecordsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration); builder.Services.AddTransportPlanningModule();
        builder.Services.AddScoped<INotificationService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService>(fixture.Realtime); builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); return app;
    }

    private static HttpClient Client(WebApplication host, Guid accountId, EmployeeRole? role = null)
    {
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        var token = new JwtServices(host.Configuration).GenerateAccessToken([new Claim(ClaimTypes.NameIdentifier, accountId.ToString()),
            new Claim(ClaimTypes.Role, role.HasValue ? JwtServices.GetRoleName(AccountType.EMPLOYEE) : JwtServices.GetRoleName(AccountType.CUSTOMER)),
            new Claim(ClaimTypes.Role, role.HasValue ? JwtServices.GetRoleName(role.Value) : JwtServices.GetRoleName(AccountType.CUSTOMER))]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
    }
}
