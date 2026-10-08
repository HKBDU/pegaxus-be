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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.NotificationService;
using RouteChange = Pegaxus.Service.RouteChangeService;

namespace Pegaxus.Service.Test;

public class RouteChangeHttpTests
{
    private const string Secret = "uc48-tests-only-secret-key-with-more-than-32-characters";

    [Fact]
    public async Task ActualJwtHttpContract_EnforcesRolesValidationDetailPaginationAndRetry()
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
        const string listPath = "/api/v1/route-change-requests";
        var createPath = $"/api/v1/route-plans/{f.Plan.RoutePlanId}/change-requests";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(listPath)).StatusCode);
        SignIn(client, f.Driver.AccountId, EmployeeRole.DRIVER);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(listPath)).StatusCode);
        SignIn(client, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(createPath, f.Proposal())).StatusCode);
        SignIn(client, f.Coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(createPath, new { reason = "Incomplete proposal" })).StatusCode);
        var proposal = f.Proposal();
        proposal.ClientRequestId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync(createPath, proposal);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var value = document.GetProperty("value");
        var id = value.GetProperty("changeRequestId").GetGuid();
        Assert.Equal("PENDING", value.GetProperty("status").GetString());
        Assert.Equal(0, value.GetProperty("createdAt").GetDateTimeOffset().Offset.Ticks);
        var repeat = await client.PostAsJsonAsync(createPath, proposal);
        Assert.Equal(id, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("changeRequestId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(listPath + "?pageSize=101")).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>(listPath + $"?transportPlanId={f.Plan.TransportPlanId}&status=PENDING");
        Assert.Equal(1, list.GetProperty("value").GetProperty("totalItems").GetInt32());
        var detail = (await client.GetFromJsonAsync<JsonElement>(listPath + "/" + id)).GetProperty("value");
        Assert.NotEmpty(detail.GetProperty("currentSegments").EnumerateArray());
        Assert.True(detail.GetProperty("comparison").GetProperty("delayMinutes").GetDecimal() >= 60);
        var decision = new RouteChange.Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = detail.GetProperty("updatedAt").GetDateTimeOffset() };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(listPath + $"/{id}/decision", decision)).StatusCode);
        SignIn(client, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var approved = await client.PostAsJsonAsync(listPath + $"/{id}/decision", decision);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("APPROVED", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(listPath + $"/{id}/decision", decision)).StatusCode);
        // A previously issued Manager JWT cannot grant an employee whose live role changed access.
        f.Manager.EmployeeAccount!.Role = EmployeeRole.DRIVER;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(listPath)).StatusCode);
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
