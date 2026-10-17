using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.MediaService;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class HorseHealthHttpTests
{
    [Theory]
    [InlineData("Customer", null)]
    [InlineData("LogisticsManager", EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData("FleetRouteCoordinator", EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData("TransportSpecialist", EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData("Driver", EmployeeRole.DRIVER)]
    [InlineData("Escort", EmployeeRole.ESCORT)]
    public async Task Http_AllSixRolesReceiveContractAndCustomerRedaction(string role, EmployeeRole? employeeRole)
    {
        await using var db = TestSupport.CreateDb();
        var f = await HorseHealthTests.Fixture.CreateAsync(db);
        if (employeeRole is EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR)
            f.Manager.Role = employeeRole.Value;
        var accountId = employeeRole switch
        {
            null => f.Customer.AccountId,
            EmployeeRole.DRIVER => f.Driver.AccountId,
            EmployeeRole.ESCORT => f.Escort.AccountId,
            _ => f.Manager.AccountId
        };
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, accountId, role);
        var root = $"/api/v1/transports/{f.Transport.TransportId}";
        var overview = await SuccessAsync(client, root + "/horse-health", "Horse health overview retrieved successfully");
        Assert.Equal(HorseHealthTests.Fixture.ServerTime, overview.GetProperty("serverTime").GetDateTimeOffset());
        Assert.Equal(f.Transport.TransportId, overview.GetProperty("transportId").GetGuid());
        var escort = overview.GetProperty("currentEscort");
        if (role == "Customer") Assert.Equal(JsonValueKind.Null, escort.GetProperty("phone").ValueKind);
        else Assert.Equal(f.Escort.Phone, escort.GetProperty("phone").GetString());
        Assert.Equal(2, overview.GetProperty("horses").GetArrayLength());
        var horse = overview.GetProperty("horses").EnumerateArray().Single(item => item.GetProperty("horse").GetProperty("horseId").GetGuid() == f.Horse.HorseId);
        Assert.Equal("AT_RISK", horse.GetProperty("latestHealthStatus").GetString()); Assert.Equal(4, horse.GetProperty("logCount").GetInt32());
        Assert.False(horse.GetProperty("latestLog").TryGetProperty("gpsCoordinates", out _));
        Assert.Equal("Restless", horse.GetProperty("latestLog").GetProperty("latestNote").GetString());
        var history = await SuccessAsync(client, root + $"/horses/{f.Horse.HorseId}/health-logs?healthStatus=AT_RISK,CRITICAL&pageSize=1",
            "Horse health logs retrieved successfully");
        Assert.Equal(0, history.GetProperty("pageIndex").GetInt32()); Assert.Equal(1, history.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, history.GetProperty("totalItems").GetInt32()); Assert.Equal(2, history.GetProperty("totalPages").GetInt32());
        var item = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(f.Latest.HorseHealthLogId, item.GetProperty("horseHealthLogId").GetGuid());
        Assert.True(item.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal(f.Horse.MicrochipNumber, item.GetProperty("horse").GetProperty("microchipNumber").GetString());
        Assert.False(item.GetProperty("horse").TryGetProperty("passportNumber", out _));
        var detail = await SuccessAsync(client, $"/api/v1/horse-health-logs/{f.Latest.HorseHealthLogId}", "Horse health log retrieved successfully");
        Assert.Equal(role == "Customer" ? null : "Internal vet note", detail.GetProperty("veterinarianNote").GetString());
        Assert.Equal(f.Recorder.EmployeeId, detail.GetProperty("checkedBy").GetProperty("employeeId").GetGuid());
        Assert.Equal("Historical Escort", detail.GetProperty("checkedBy").GetProperty("fullName").GetString());
        Assert.Equal(38.2m, detail.GetProperty("temperatureCelsius").GetDecimal());
        Assert.Equal("Cleaned", detail.GetProperty("treatmentProvided").GetString());
        Assert.Equal("Medication", detail.GetProperty("medicationGiven").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("gpsCoordinates").ValueKind);
        Assert.Equal(HorseHealthTests.Storage.SignedUrl, detail.GetProperty("photos")[0].GetProperty("photoUrl").GetString());
        foreach (var field in new[] { "latitude", "longitude", "capturedAt", "caption" })
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("photos")[0].GetProperty(field).ValueKind);
        Assert.Equal(HorseHealthTests.Fixture.ServerTime, detail.GetProperty("serverTime").GetDateTimeOffset());
        Assert.DoesNotContain(f.Latest.PhotoUrl!, detail.GetRawText());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Http_ValidationUsesFieldErrorsAndDefaultPagination()
    {
        await using var db = TestSupport.CreateDb();
        var f = await HorseHealthTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Customer.AccountId, "Customer");
        var path = $"/api/v1/transports/{f.Transport.TransportId}/horses/{f.Horse.HorseId}/health-logs";
        var result = await SuccessAsync(client, path, "Horse health logs retrieved successfully");
        Assert.Equal(0, result.GetProperty("pageIndex").GetInt32()); Assert.Equal(20, result.GetProperty("pageSize").GetInt32());
        foreach (var query in new[] { "logType=", "logType=1", "logType=OTHER,", "logType=other", "healthStatus=BOGUS", "healthStatus=1",
                     "pageIndex=-1", "pageIndex=2147483647", "pageIndex=2147483648", "pageSize=0", "pageSize=101" })
        {
            var body = await ErrorAsync(await client.GetAsync(path + "?" + query), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(body.GetProperty("errors").EnumerateArray(), error => error.GetProperty("field").GetString() == query.Split('=')[0]);
        }
        foreach (var invalid in new[] { "not-a-guid", Guid.Empty.ToString() })
        {
            await ErrorAsync(await client.GetAsync($"/api/v1/transports/{invalid}/horse-health"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            await ErrorAsync(await client.GetAsync($"/api/v1/horse-health-logs/{invalid}"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            await ErrorAsync(await client.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/horses/{invalid}/health-logs"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }
        Assert.Empty(f.Storage.Reads);
    }

    [Fact]
    public async Task Http_HidesForeignResourcesAndRejectsForgedCrewRoleBeforeSigning()
    {
        await using var db = TestSupport.CreateDb();
        var f = await HorseHealthTests.Fixture.CreateAsync(db);
        var (other, _) = await TestSupport.AddCustomerAsync(db);
        f.Segment.DriverEmployeeId = null;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var host = await StartAsync(db, f.Storage);
        var paths = new[] { $"/api/v1/transports/{f.Transport.TransportId}/horse-health",
            $"/api/v1/transports/{f.Transport.TransportId}/horses/{f.Horse.HorseId}/health-logs", $"/api/v1/horse-health-logs/{f.Latest.HorseHealthLogId}" };
        using var anonymous = new HttpClient { BaseAddress = Address(host) };
        using var foreign = Client(host, other.AccountId, "Customer");
        using var forged = Client(host, f.Driver.AccountId, "LogisticsManager");
        using var invalidRole = Client(host, f.Manager.AccountId, "Admin");
        foreach (var path in paths)
        {
            await ErrorAsync(await anonymous.GetAsync(path), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
            await ErrorAsync(await foreign.GetAsync(path), HttpStatusCode.NotFound, path == paths[2] ? "HEALTH_LOG_NOT_FOUND" : "TRANSPORT_NOT_FOUND");
            var denied = await ErrorAsync(await forged.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
            Assert.Equal("You are not assigned to this transport", denied.GetProperty("message").GetString());
            await ErrorAsync(await invalidRole.GetAsync(path), HttpStatusCode.Forbidden, "FORBIDDEN");
        }
        using var owner = Client(host, f.Customer.AccountId, "Customer");
        await ErrorAsync(await owner.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/horses/{Guid.NewGuid()}/health-logs"), HttpStatusCode.NotFound, "HORSE_NOT_FOUND");
        db.Attach(f.Transport); f.Transport.Status = TransportStatus.CANCELLED; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        foreach (var path in paths)
            await ErrorAsync(await owner.GetAsync(path), HttpStatusCode.Conflict, "INVALID_TRANSPORT_STATUS");
        db.Attach(f.Customer); f.Customer.IsDeleted = true; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await ErrorAsync(await owner.GetAsync(paths[0]), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        Assert.Empty(f.Storage.Reads);
    }

    internal static async Task<WebApplication> StartAsync(AppDbContext db, HorseHealthTests.Storage storage)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "horse-health-tests", ["JwtOptions:Audience"] = "horse-health-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<TimeProvider>(new HorseHealthTests.FixedClock());
        builder.Services.AddSingleton<IMediaService>(storage); builder.Services.AddSingleton<IDocumentStorageService>(storage);
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(HorseHealthLogsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddTransportPlanningModule();
        builder.Services.AddErrorHandling();
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); return app;
    }

    internal static HttpClient Client(WebApplication host, Guid accountId, string role)
    {
        var client = new HttpClient { BaseAddress = Address(host) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)]));
        return client;
    }

    private static Uri Address(WebApplication host) =>
        new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    private static async Task<JsonElement> SuccessAsync(HttpClient client, string path, string message)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal(message, body.GetProperty("message").GetString());
        return body.GetProperty("value");
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString());
            return body;
        }
    }
}
