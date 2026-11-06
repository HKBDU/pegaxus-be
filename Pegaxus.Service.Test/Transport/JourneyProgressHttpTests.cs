using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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
using Pegaxus.Service.JwtService;

namespace Pegaxus.Service.Test;

public class JourneyProgressHttpTests
{
    [Fact]
    public async Task Http_ReturnsAllFourContractsAndHidesCustomerInternalFields()
    {
        await using var db = TestSupport.CreateDb();
        var f = await JourneyProgressTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Clock);
        using var manager = Client(host, f.Manager.AccountId, "LogisticsManager");
        var path = $"/api/v1/transports/{f.Transport.TransportId}";
        var ongoing = await manager.GetFromJsonAsync<JsonElement>("/api/v1/transports/ongoing");
        Assert.Equal(200, ongoing.GetProperty("code").GetInt32());
        Assert.Equal("Ongoing transports retrieved successfully", ongoing.GetProperty("message").GetString());
        var value = ongoing.GetProperty("value");
        Assert.Equal(f.Clock.GetUtcNow(), value.GetProperty("serverTime").GetDateTimeOffset());
        Assert.Equal(0, value.GetProperty("pageIndex").GetInt32());
        Assert.Equal(20, value.GetProperty("pageSize").GetInt32());
        Assert.Equal(new[] { "activeIncidentCount", "bookingCode", "bookingId", "currentSegment", "customerName", "departureAt", "horseCount", "routeType", "status", "transportCode", "transportId" },
            value.GetProperty("items")[0].EnumerateObject().Select(item => item.Name).Order());
        using var driver = Client(host, f.Driver.AccountId, "Driver");
        var assigned = (await driver.GetFromJsonAsync<JsonElement>("/api/v1/transports/assigned-to-me")).GetProperty("value");
        Assert.Equal("DRIVER", Assert.Single(assigned.GetProperty("items").EnumerateArray()).GetProperty("myRole").GetString());
        Assert.False(assigned.TryGetProperty("pageIndex", out _));
        var staff = (await manager.GetFromJsonAsync<JsonElement>(path + "/overview")).GetProperty("value");
        Assert.Equal(3, staff.GetProperty("horseCount").GetInt32());
        Assert.Equal(1, staff.GetProperty("horseHealthSummary").GetProperty("critical").GetInt32());
        Assert.Equal("private-phone", staff.GetProperty("currentSegment").GetProperty("driver").GetProperty("phone").GetString());
        Assert.Equal("CRITICAL", staff.GetProperty("activeIncidents")[0].GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, staff.GetProperty("complianceDossiers")[0].GetProperty("dossierType").ValueKind);
        using var customer = Client(host, f.Customer.AccountId, "Customer");
        var owner = (await customer.GetFromJsonAsync<JsonElement>(path + "/overview")).GetProperty("value");
        var current = owner.GetProperty("currentSegment");
        Assert.Equal(JsonValueKind.Null, current.GetProperty("driver").GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, current.GetProperty("escort").GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, current.GetProperty("note").ValueKind);
        Assert.Empty(current.GetProperty("vehicles").EnumerateArray());
        Assert.All(owner.GetProperty("activeIncidents").EnumerateArray(), item =>
        {
            Assert.False(item.TryGetProperty("severity", out _));
            Assert.False(item.TryGetProperty("description", out _));
        });
        var segments = (await customer.GetFromJsonAsync<JsonElement>(path + "/segments")).GetProperty("value");
        Assert.Equal(3, segments.GetProperty("segments").GetArrayLength());
        Assert.Equal(4, segments.GetProperty("handoverBoundaries").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, segments.GetProperty("handoverBoundaries")[1].GetProperty("existingHandover").ValueKind);
        Assert.All(segments.GetProperty("handoverBoundaries").EnumerateArray(), item => Assert.False(item.GetProperty("canCreate").GetBoolean()));
        using var outsider = Client(host, f.OtherCustomer.AccountId, "Customer");
        foreach (var endpoint in new[] { "/overview", "/segments" })
            await Error(await outsider.GetAsync(path + endpoint), HttpStatusCode.NotFound, "TRANSPORT_NOT_FOUND");
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200, 403)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 200, 403)]
    [InlineData(EmployeeRole.DRIVER, 403, 200)]
    [InlineData(EmployeeRole.ESCORT, 403, 200)]
    public async Task Http_EnforcesRoleMatrixAndSegmentAssignment(EmployeeRole role, int ongoingStatus, int assignedStatus)
    {
        await using var db = TestSupport.CreateDb();
        var f = await JourneyProgressTests.Fixture.CreateAsync(db);
        var employee = role switch { EmployeeRole.LOGISTICS_MANAGER => f.Manager, EmployeeRole.FLEET_ROUTE_COORDINATOR => f.Coordinator,
            EmployeeRole.TRANSPORT_SPECIALIST => f.Specialist, EmployeeRole.DRIVER => f.Driver, _ => f.Escort };
        await using var host = await StartAsync(db, f.Clock);
        using var client = Client(host, employee.AccountId, JwtServices.GetRoleName(role));
        Assert.Equal(ongoingStatus, (int)(await client.GetAsync("/api/v1/transports/ongoing")).StatusCode);
        Assert.Equal(assignedStatus, (int)(await client.GetAsync("/api/v1/transports/assigned-to-me")).StatusCode);
        var path = $"/api/v1/transports/{f.Transport.TransportId}";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path + "/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path + "/segments")).StatusCode);
        if (role is EmployeeRole.DRIVER or EmployeeRole.ESCORT)
        {
            var unassigned = await JourneyProgressTests.Fixture.EmployeeAsync(db, role);
            using var other = Client(host, unassigned.AccountId, JwtServices.GetRoleName(role));
            await Error(await other.GetAsync(path + "/overview"), HttpStatusCode.Forbidden, "FORBIDDEN");
            await Error(await other.GetAsync(path + "/segments"), HttpStatusCode.Forbidden, "FORBIDDEN");
        }
    }

    [Fact]
    public async Task Http_RejectsInvalidQueriesIdsTokensAndStaleRoles()
    {
        await using var db = TestSupport.CreateDb();
        var f = await JourneyProgressTests.Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Clock);
        using var manager = Client(host, f.Manager.AccountId, "LogisticsManager");
        foreach (var (query, field) in new[] { ("status=CREATED", "status"), ("status=2", "status"), ("status=READY,", "status"),
                     ("pageIndex=-1", "pageIndex"), ("pageIndex=abc", "pageIndex"), ("pageIndex=2147483647", "pageIndex"),
                     ("pageSize=0", "pageSize"), ("pageSize=101", "pageSize"), ("sortBy=unknown", "sortBy"), ("hasActiveIncident=unknown", "hasActiveIncident") })
        {
            var error = await Error(await manager.GetAsync("/api/v1/transports/ongoing?" + query), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), item => item.GetProperty("field").GetString() == field);
        }
        foreach (var id in new[] { "invalid", Guid.Empty.ToString() })
        foreach (var endpoint in new[] { "overview", "segments" })
            await Error(await manager.GetAsync($"/api/v1/transports/{id}/{endpoint}"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await manager.GetAsync($"/api/v1/transports/{Guid.NewGuid()}/overview"), HttpStatusCode.NotFound, "TRANSPORT_NOT_FOUND");
        f.Transport.Status = TransportStatus.PLANNING;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/overview"), HttpStatusCode.Conflict, "INVALID_TRANSPORT_STATUS");
        using var customer = Client(host, f.Customer.AccountId, "Customer");
        await Error(await customer.GetAsync("/api/v1/transports/ongoing"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await Error(await customer.GetAsync("/api/v1/transports/assigned-to-me"), HttpStatusCode.Forbidden, "FORBIDDEN");
        f.Manager.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync("/api/v1/transports/ongoing"), HttpStatusCode.Forbidden, "FORBIDDEN");
        f.Manager.IsActive = false;
        await db.SaveChangesAsync();
        await Error(await manager.GetAsync("/api/v1/transports/ongoing"), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
        manager.DefaultRequestHeaders.Authorization = null;
        await Error(await manager.GetAsync("/api/v1/transports/ongoing"), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, TimeProvider clock)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "journey-tests", ["JwtOptions:Audience"] = "journey-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(clock);
        builder.Services.AddControllers().AddApplicationPart(typeof(TransportsController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(item => item.Value!.Errors.Count > 0).SelectMany(item => item.Value!.Errors.Select(error => new {
                    field = string.IsNullOrEmpty(item.Key) ? "$" : char.ToLowerInvariant(item.Key[0]) + item.Key[1..], message = error.ErrorMessage })),
                "The request data is invalid", traceId: context.HttpContext.TraceIdentifier)));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddTransportPlanningModule();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication host, Guid accountId, string role)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)]));
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
