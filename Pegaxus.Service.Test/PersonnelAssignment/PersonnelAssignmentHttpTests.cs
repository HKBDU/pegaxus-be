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
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.Models;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.PersonnelAssignmentService.Validator;
using Request = Pegaxus.Service.PersonnelAssignmentService.Request;
using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class PersonnelAssignmentHttpTests
{
    [Fact]
    public async Task Http_Phase1AndAssignable_IncludeAccountContactAssignmentDatesAndDossier()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var specialist = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST);
        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var replacement = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        specialist.Account.AvatarUrl = "https://example.com/specialist.png";
        replacement.Account.AvatarUrl = "https://example.com/coordinator.png";
        var (route, _) = await PersonnelAssignmentTests.RouteAsync(db, RoutePlanStatus.DRAFT);
        db.ChangeTracker.Clear();
        await using var host = await Start(db);
        using var client = Client(host, manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var assignable = await client.GetFromJsonAsync<JsonElement>("/api/v1/employees/assignable?role=TRANSPORT_SPECIALIST");
        Contact(assignable.GetProperty("value").GetProperty("items")[0], specialist);
        var url = $"/api/v1/transport-plans/{route.TransportPlanId}/assignments/phase-1";
        using var assigned = await client.PostAsJsonAsync(url, new {
            transportSpecialistId = specialist.EmployeeId, routeCoordinatorId = coordinator.EmployeeId });
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var phase1 = (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        var assignedEmployees = phase1.GetProperty("assignedEmployees").EnumerateArray().ToArray();
        var assignedSpecialist = assignedEmployees.Single(x => x.GetProperty("employeeId").GetGuid() == specialist.EmployeeId);
        var assignedCoordinator = assignedEmployees.Single(x => x.GetProperty("employeeId").GetGuid() == coordinator.EmployeeId);
        Contact(assignedSpecialist, specialist); Contact(assignedCoordinator, coordinator);
        db.ChangeTracker.Clear();
        var view = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).GetProperty("value");
        Assert.Equal(JsonValueKind.Null, view.GetProperty("complianceDossier").ValueKind);
        var phase1View = view.GetProperty("phase1");
        Contact(phase1View.GetProperty("transportSpecialist"), specialist);
        Contact(phase1View.GetProperty("routeCoordinator"), coordinator);
        Assert.Equal(assignedSpecialist.GetProperty("assignedAt").GetDateTimeOffset(), phase1View.GetProperty("transportSpecialist").GetProperty("assignedAt").GetDateTimeOffset());
        Assert.Equal(assignedCoordinator.GetProperty("assignedAt").GetDateTimeOffset(), phase1View.GetProperty("routeCoordinator").GetProperty("assignedAt").GetDateTimeOffset());
        var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), TransportId = route.TransportPlan.TransportId,
            DossierCode = "DOSSIER-CONTACT", Status = ComplianceDossierStatus.DRAFT, CreatedAt = PersonnelAssignmentTests.Now,
            UpdatedAt = PersonnelAssignmentTests.Now, OpenedAt = PersonnelAssignmentTests.Now };
        db.ComplianceDossiers.Add(dossier); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        using var changed = await client.PutAsJsonAsync(url, new { lastUpdatedAt = phase1.GetProperty("updatedAt").GetDateTimeOffset(),
            routeCoordinatorId = replacement.EmployeeId, reason = "On leave" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var changedEmployees = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("assignedEmployees").EnumerateArray().ToArray();
        Assert.Equal(2, changedEmployees.Length);
        Contact(changedEmployees.Single(x => x.GetProperty("employeeId").GetGuid() == specialist.EmployeeId), specialist);
        var changedCoordinator = changedEmployees.Single(x => x.GetProperty("employeeId").GetGuid() == replacement.EmployeeId);
        Contact(changedCoordinator, replacement);
        db.ChangeTracker.Clear();
        view = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).GetProperty("value");
        var dossierView = view.GetProperty("complianceDossier");
        Assert.Equal(dossier.ComplianceDossierId, dossierView.GetProperty("complianceDossierId").GetGuid());
        Assert.Equal(dossier.DossierCode, dossierView.GetProperty("dossierCode").GetString());
        Assert.Equal("DRAFT", dossierView.GetProperty("status").GetString());
        Contact(view.GetProperty("phase1").GetProperty("routeCoordinator"), replacement);
        Assert.Equal(changedCoordinator.GetProperty("assignedAt").GetDateTimeOffset(), view.GetProperty("phase1").GetProperty("routeCoordinator").GetProperty("assignedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Http_AssignAndReplace_ScheduleConflictIdentifiesRejectedEmployeeAndBlockingSegment(bool replace, bool conflictEscort)
    {
        await using var db = TestSupport.CreateDb();
        var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var otherDriver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var otherEscort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var (route, segment) = await PersonnelAssignmentTests.RouteAsync(db);
        var (blockingRoute, blockingSegment) = await PersonnelAssignmentTests.RouteAsync(db);
        blockingSegment.Route.FromLocation.LocationName = "Lyon";
        blockingSegment.Route.ToLocation.LocationName = "Paris";
        blockingSegment.PlannedDepartureAt = segment.PlannedDepartureAt!.Value.AddMinutes(15);
        blockingSegment.PlannedArrivalAt = segment.PlannedArrivalAt!.Value.AddHours(1);
        blockingSegment.DriverEmployeeId = conflictEscort ? otherDriver.EmployeeId : driver.EmployeeId;
        blockingSegment.EscortEmployeeId = conflictEscort ? escort.EmployeeId : otherEscort.EmployeeId;
        if (replace)
        {
            segment.DriverEmployeeId = conflictEscort ? driver.EmployeeId : otherDriver.EmployeeId;
            segment.EscortEmployeeId = conflictEscort ? otherEscort.EmployeeId : escort.EmployeeId;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using var host = await Start(db);
        using var client = Client(host, manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        using var response = replace
            ? await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/segments/{segment.RoutePlanRouteId}/personnel",
                new Request.ReplaceRequest { LastUpdatedAt = PersonnelAssignmentTests.Version(route), Reason = "Sick",
                    DriverEmployeeId = conflictEscort ? null : driver.EmployeeId, EscortEmployeeId = conflictEscort ? escort.EmployeeId : null })
            : await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", PersonnelAssignmentTests.Assign(route, segment, driver, escort));
        var value = (await Error(response, HttpStatusCode.Conflict, "SCHEDULE_CONFLICT")).GetProperty("value");
        var employee = conflictEscort ? escort : driver;
        Assert.Equal(employee.EmployeeId, value.GetProperty("employeeId").GetGuid());
        Assert.Equal(employee.EmployeeCode, value.GetProperty("employeeCode").GetString());
        Assert.Equal(employee.Role.ToString(), value.GetProperty("role").GetString());
        Assert.Equal(segment.RoutePlanRouteId, value.GetProperty("routePlanRouteId").GetGuid());
        Assert.Equal(blockingRoute.TransportPlan.PlanCode, value.GetProperty("conflictingTransportPlanCode").GetString());
        Assert.Equal("Lyon", value.GetProperty("conflictingFromLocationName").GetString());
        Assert.Equal("Paris", value.GetProperty("conflictingToLocationName").GetString());
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(blockingSegment.PlannedDepartureAt!.Value, DateTimeKind.Utc)), value.GetProperty("conflictingPlannedDepartureAt").GetDateTimeOffset());
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(blockingSegment.PlannedArrivalAt!.Value, DateTimeKind.Utc)), value.GetProperty("conflictingPlannedArrivalAt").GetDateTimeOffset());
        var unchanged = await db.RoutePlanRoutes.AsNoTracking().SingleAsync(x => x.RoutePlanRouteId == segment.RoutePlanRouteId);
        Assert.Equal(segment.DriverEmployeeId, unchanged.DriverEmployeeId); Assert.Equal(segment.EscortEmployeeId, unchanged.EscortEmployeeId);
        Assert.Equal(route.UpdatedAt, (await db.RoutePlans.AsNoTracking().SingleAsync(x => x.RoutePlanId == route.RoutePlanId)).UpdatedAt);
        Assert.Empty(db.CatalogAuditLogs); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task Http_ThreeRoutes_EnvelopesValidationAndVersions()
    {
        await using var db = TestSupport.CreateDb();
        var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var replacement = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        driver.Account.AvatarUrl = "https://example.com/driver.png";
        replacement.Account.AvatarUrl = "https://example.com/replacement.png";
        var (route, segment) = await PersonnelAssignmentTests.RouteAsync(db, RoutePlanStatus.DRAFT);
        await using var host = await Start(db);
        using var client = Client(host, manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var employees = await client.GetFromJsonAsync<JsonElement>("/api/v1/employees/assignable?role=TRANSPORT_SPECIALIST");
        Assert.Equal(200, employees.GetProperty("code").GetInt32());
        await Error(await client.GetAsync("/api/v1/employees/assignable?role=DRIVER"), HttpStatusCode.BadRequest, "INVALID_DATE_RANGE");
        var view = await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-plans/{route.TransportPlanId}/assignments");
        Assert.False(view.GetProperty("value").GetProperty("phase2").GetProperty("enabled").GetBoolean());
        var request = PersonnelAssignmentTests.Assign(route, segment, driver, escort);
        await Error(await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request), HttpStatusCode.UnprocessableEntity, "ROUTE_NOT_CONFIRMED");
        route.Status = RoutePlanStatus.CONFIRMED; await db.SaveChangesAsync();
        var assigned = await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request);
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var value = (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.True(value.GetProperty("isPersonnelComplete").GetBoolean());
        Assert.Equal(driver.EmployeeId, value.GetProperty("segments")[0].GetProperty("driver").GetProperty("employeeId").GetGuid());
        Contact(value.GetProperty("segments")[0].GetProperty("driver"), driver);
        Contact(value.GetProperty("segments")[0].GetProperty("escort"), escort);
        var version = value.GetProperty("updatedAt").GetDateTimeOffset();
        await Error(await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", new { lastUpdatedAt = version }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", request), HttpStatusCode.Conflict, "ROUTE_PLAN_MODIFIED");
        var patchUrl = $"/api/v1/route-plans/{route.RoutePlanId}/segments/{segment.RoutePlanRouteId}/personnel";
        var changed = await client.PutAsJsonAsync(patchUrl, new Request.ReplaceRequest { LastUpdatedAt = version, DriverEmployeeId = replacement.EmployeeId, Reason = "Sick" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var changedValue = (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(replacement.EmployeeId, changedValue.GetProperty("segment").GetProperty("driver").GetProperty("employeeId").GetGuid());
        Assert.Equal(escort.EmployeeId, changedValue.GetProperty("segment").GetProperty("escort").GetProperty("employeeId").GetGuid());
        Contact(changedValue.GetProperty("segment").GetProperty("driver"), replacement);
        view = await client.GetFromJsonAsync<JsonElement>($"/api/v1/transport-plans/{route.TransportPlanId}/assignments");
        Contact(view.GetProperty("value").GetProperty("phase2").GetProperty("segments")[0].GetProperty("driver"), replacement);
        Contact(view.GetProperty("value").GetProperty("phase2").GetProperty("segments")[0].GetProperty("escort"), escort);
        await Error(await client.PutAsJsonAsync(patchUrl, new { driverEmployeeId = driver.EmployeeId, reason = "Sick" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.PutAsJsonAsync(patchUrl, new Request.ReplaceRequest { LastUpdatedAt = changedValue.GetProperty("updatedAt").GetDateTimeOffset(), DriverEmployeeId = driver.EmployeeId }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await Error(await client.GetAsync($"/api/v1/transport-plans/{Guid.NewGuid()}/assignments"), HttpStatusCode.NotFound, "TRANSPORT_PLAN_NOT_FOUND");
        await Error(await client.PostAsJsonAsync($"/api/v1/route-plans/{Guid.NewGuid()}/personnel", request), HttpStatusCode.NotFound, "ROUTE_PLAN_NOT_FOUND");
        await Error(await client.PutAsJsonAsync(patchUrl, new { lastUpdatedAt = version, driverEmployeeId = driver.EmployeeId, reason = "Sick", transportProviderId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200, 201)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403, 403)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 403, 403)]
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
        Assert.Equal(writeStatus, (int)(await client.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", PersonnelAssignmentTests.Assign(route, segment, driver, escort))).StatusCode);
        Assert.Equal(writeStatus == 201 ? 200 : writeStatus, (int)(await client.PutAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/segments/{segment.RoutePlanRouteId}/personnel", new Request.ReplaceRequest {
            LastUpdatedAt = PersonnelAssignmentTests.Version(route), DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId, Reason = "Confirm" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/transport-plans/{route.TransportPlanId}/assignments")).StatusCode);
        using var forged = Client(host, actor.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        if (role != EmployeeRole.LOGISTICS_MANAGER)
            Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync($"/api/v1/route-plans/{route.RoutePlanId}/personnel", PersonnelAssignmentTests.Assign(route, segment, driver, escort))).StatusCode);
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
        builder.Services.AddScoped<Pegaxus.Service.TransportPlanService.IService, Pegaxus.Service.TransportPlanService.Service>();
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
    private static async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(code, body.GetProperty("errorCode").GetString());
        return body;
    }
    private static void Contact(JsonElement value, Employee employee)
    {
        Assert.Equal(employee.EmployeeId, value.GetProperty("employeeId").GetGuid());
        Assert.Equal(employee.Account.Email, value.GetProperty("email").GetString());
        Assert.Equal(employee.Account.AvatarUrl, value.GetProperty("avatarUrl").GetString());
    }
}
