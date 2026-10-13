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

using RouteChange = Pegaxus.Service.RouteChangeService;

namespace Pegaxus.Service.Test;

public class RouteChangeHttpTests
{
    private const string Secret = "uc48-tests-only-secret-key-with-more-than-32-characters";

    [Fact]
    public async Task ActualJwtHttpContract_FourEndpoints_EnforceRolesValidationAndVersions()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RouteChangeRequestsController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "uc48-test",
            ["JwtOptions:Audience"] = "uc48-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddControllers().AddApplicationPart(typeof(RouteChangeRequestsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<RouteChange.IService>(_ => f.Service);
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var backup = await f.AlternateAsync();
        var createPath = $"/api/v1/route-plans/{backup.RoutePlanId}/change-request";
        var decisionPath = $"/api/v1/route-plans/{backup.RoutePlanId}/change-decision";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(createPath, f.Proposal())).StatusCode);
        SignIn(client, f.Driver.AccountId, EmployeeRole.DRIVER);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(createPath, f.Proposal())).StatusCode);
        SignIn(client, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(createPath, f.Proposal())).StatusCode);
        SignIn(client, f.Coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(createPath, new { reason = "short" })).StatusCode);
        var response = await client.PostAsJsonAsync(createPath, f.Proposal());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(backup.RoutePlanId, value.GetProperty("routePlanId").GetGuid());
        Assert.Equal("CONFIRMED", value.GetProperty("status").GetString());
        Assert.Equal("BACKUP", value.GetProperty("routeType").GetString());
        Assert.False(value.TryGetProperty("changeRequestId", out _));
        var decision = new RouteChange.Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(decisionPath, decision)).StatusCode);
        SignIn(client, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var approved = await client.PostAsJsonAsync(decisionPath, decision);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("ACTIVE", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(decisionPath, decision)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/route-change-requests")).StatusCode);
        f.Manager.EmployeeAccount!.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(decisionPath, decision)).StatusCode);
        f.Manager.EmployeeAccount.Role = EmployeeRole.LOGISTICS_MANAGER; await db.SaveChangesAsync();
        var returning = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == returning.RoutePlanId);
        var vehicle = await db.Vehicles.SingleAsync();
        // Return begins at the current safe stop; this test uses a separate confirmed route before backup execution.
        var activation = await client.PostAsJsonAsync($"/api/v1/route-plans/{returning.RoutePlanId}/return-activation",
            new RouteChange.Request.ActivationRequest { LastUpdatedAt = RouteChangeTests.Fixture.Version(returning),
                Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleIds = [vehicle.VehicleId] }] });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        var active = (await activation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("RETURN", active.GetProperty("routeType").GetString());
        Assert.True(active.GetProperty("segments")[0].TryGetProperty("isOutsourced", out _));
        SignIn(client, f.Coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var notice = await client.PostAsJsonAsync($"/api/v1/route-plans/{returning.RoutePlanId}/driver-notice", new { message = "Return now" });
        Assert.Equal(HttpStatusCode.OK, notice.StatusCode);
        Assert.Equal(f.Driver.EmployeeId, (await notice.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("notifiedDrivers")[0].GetProperty("employeeId").GetGuid());
        await app.StopAsync();
    }

    private static void SignIn(HttpClient client, Guid accountId, EmployeeRole role)
    {
        var token = new JwtSecurityToken("uc48-test", "uc48-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, JwtServices.GetRoleName(role))],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
