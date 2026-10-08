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
using Pegaxus.Service.JwtService;
using Pegaxus.Service.Models;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.PersonnelAssignmentService.Validator;
using Request = Pegaxus.Service.PersonnelAssignmentService.Request;

namespace Pegaxus.Service.Test;

public class PersonnelAssignmentHttpTests
{
    [Fact]
    public async Task Http_ThreeRoutes_EnvelopesValidationAndVersions()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var replacement = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var (route, segment) = await PersonnelAssignmentTests.RouteAsync(db, RoutePlanStatus.DRAFT);
        await using var host = await Start(db);
        using var client = Client(host, manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var view = await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-plans/{route.TransportPlanId}/assignments");
        Assert.False(view.GetProperty("value").GetProperty("phase2").GetProperty("enabled").GetBoolean());
        var request = PersonnelAssignmentTests.Assign(route, segment, driver, escort);
        await Error(await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request), HttpStatusCode.UnprocessableEntity, "ROUTE_NOT_APPROVED");
        route.Status = RoutePlanStatus.CONFIRMED; await db.SaveChangesAsync();
        var assigned = await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var value = (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("FULLY_ASSIGNED", value.GetProperty("personnelStatus").GetString());
        Assert.Equal(driver.EmployeeId, value.GetProperty("segments")[0].GetProperty("driver").GetProperty("employeeId").GetGuid());
        var version = value.GetProperty("updatedAt").GetDateTimeOffset();
        await Error(await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", new { lastUpdatedAt = version }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request), HttpStatusCode.Conflict, "ROUTE_PLAN_MODIFIED");
        var patchUrl = $"/api/v1/route-plans/{route.RoutePlanId}/segments/{segment.RoutePlanRouteId}/personnel";
        var changed = await client.PatchAsJsonAsync(patchUrl, new Request.ReplaceRequest { LastUpdatedAt = version, DriverEmployeeId = replacement.EmployeeId, Reason = "Sick" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var changedValue = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(replacement.EmployeeId, changedValue.GetProperty("segment").GetProperty("driver").GetProperty("employeeId").GetGuid());
        Assert.Equal(escort.EmployeeId, changedValue.GetProperty("segment").GetProperty("escort").GetProperty("employeeId").GetGuid());
        await Error(await client.PatchAsJsonAsync(patchUrl, new { driverEmployeeId = driver.EmployeeId, reason = "Sick" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.PatchAsJsonAsync(patchUrl, new Request.ReplaceRequest { LastUpdatedAt = changedValue.GetProperty("updatedAt").GetDateTimeOffset(), DriverEmployeeId = driver.EmployeeId }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.GetAsync($"/api/v1/transport-plans/{Guid.NewGuid()}/assignments"), HttpStatusCode.NotFound, "TRANSPORT_PLAN_NOT_FOUND");
        await Error(await client.PutAsJsonAsync($"/api/v1/route-plans/{Guid.NewGuid()}/personnel", request), HttpStatusCode.NotFound, "ROUTE_PLAN_NOT_FOUND");
        await Error(await client.PatchAsJsonAsync(patchUrl, new { lastUpdatedAt = version, driverEmployeeId = driver.EmployeeId, reason = "Sick", transportProviderId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200, 200)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 200, 403)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200, 403)]
    [InlineData(EmployeeRole.DRIVER, 403, 403)]
    [InlineData(EmployeeRole.ESCORT, 403, 403)]
    public async Task JwtPolicies_AndDatabaseRolesProtectReadAndWrite(EmployeeRole role, int readStatus, int writeStatus)
    {
        await using var db = TestSupport.CreateDb();
        var actor = await PersonnelAssignmentTests.EmployeeAsync(db, role);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await PersonnelAssignmentTests.RouteAsync(db);
        await using var host = await Start(db);
        using var client = Client(host, actor.AccountId, role);
        Assert.Equal(readStatus, (int)(await client.GetAsync($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).StatusCode);
        Assert.Equal(writeStatus, (int)(await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", PersonnelAssignmentTests.Assign(route, segment, driver, escort))).StatusCode);
        Assert.Equal(writeStatus, (int)(await client.PatchAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/segments/{segment.RoutePlanRouteId}/personnel", new Request.ReplaceRequest {
            LastUpdatedAt = PersonnelAssignmentTests.Version(route), DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId, Reason = "Confirm" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).StatusCode);
        using var forged = Client(host, actor.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        if (role != EmployeeRole.LOGISTICS_MANAGER)
            Assert.Equal(HttpStatusCode.Forbidden, (await forged.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", PersonnelAssignmentTests.Assign(route, segment, driver, escort))).StatusCode);
        actor.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).StatusCode);
    }

    private static async Task<WebApplication> Start(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "personnel-tests", ["JwtOptions:Audience"] = "personnel-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration); builder.Services.AddSingleton(db);
        builder.Services.AddSingleton(PersonnelAssignmentTests.TestClock);
        builder.Services.AddControllers().AddApplicationPart(typeof(PersonnelAssignmentsController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(null, "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.PersonnelAssignmentService.IService, Pegaxus.Service.PersonnelAssignmentService.Service>();
        builder.Services.AddScoped<IValidator<Request.AssignRequest>, AssignRequestValidator>(); builder.Services.AddScoped<IValidator<Request.ReplaceRequest>, ReplaceRequestValidator>();
        builder.Services.AddScoped<Pegaxus.Service.NotificationService.IService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); return app;
    }
    private static HttpClient Client(WebApplication host, Guid accountId, EmployeeRole role)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, "Employee"), new Claim(ClaimTypes.Role, JwtServices.GetRoleName(role))]));
        return client;
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString());
    }
}
