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
using Pegaxus.Service.VehicleService;
using Pegaxus.Service.VehicleService.Validator;
using Request = Pegaxus.Service.VehicleService.Request;

namespace Pegaxus.Service.Test;

public class VehicleHttpTests
{
    [Fact]
    public async Task HttpContract_AllEndpoints_ValidationVersionsAndAuthentication()
    {
        await using var db = TestSupport.CreateDb();
        var (account, _) = await TestSupport.AddLogisticsManagerAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, account.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var created = await client.PostAsJsonAsync("/api/v1/vehicles", VehicleServiceTests.ValidRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vehicle = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        var id = vehicle.GetProperty("vehicleId").GetGuid();
        var version = vehicle.GetProperty("updatedAt").GetDateTimeOffset();
        Assert.Equal("ROAD", vehicle.GetProperty("transportMode").GetString());
        Assert.False(vehicle.TryGetProperty("isAvailable", out _));
        Assert.EndsWith($"/api/v1/vehicles/{id}", created.Headers.Location!.ToString());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/vehicles?availableFrom=2026-10-10&availableTo=2026-10-11");
        Assert.True(list.GetProperty("value").GetProperty("items")[0].GetProperty("isAvailable").GetBoolean());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/vehicles/{id}");
        Assert.Empty(detail.GetProperty("value").GetProperty("upcomingAssignments").EnumerateArray());
        var update = new Request.UpdateRequest { LastUpdatedAt = version, LicensePlate = "29C-99988", CapacityHorses = 4 };
        var updated = await client.PutAsJsonAsync($"/api/v1/vehicles/{id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        version = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("updatedAt").GetDateTimeOffset();
        var status = await client.PatchAsJsonAsync($"/api/v1/vehicles/{id}/status", VehicleServiceTests.StatusRequest(version, "SUSPENDED"));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var changed = (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("AVAILABLE", changed.GetProperty("previousStatus").GetString());
        Assert.Equal("SUSPENDED", changed.GetProperty("status").GetString());
        await Error(await client.PutAsJsonAsync($"/api/v1/vehicles/{id}", update), HttpStatusCode.Conflict, "VEHICLE_MODIFIED");
        await Error(await client.GetAsync($"/api/v1/vehicles/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "VEHICLE_NOT_FOUND");
        await Error(await client.GetAsync("/api/v1/vehicles?availableFrom=2026-10-10"), HttpStatusCode.BadRequest, "INVALID_DATE_RANGE");
        await Error(await client.GetAsync("/api/v1/vehicles?sortBy=bad"), HttpStatusCode.BadRequest, "INVALID_SORT_PARAMETER");
        await Error(await client.PostAsJsonAsync("/api/v1/vehicles", VehicleServiceTests.ValidRequest()), HttpStatusCode.Conflict, "VEHICLE_CODE_DUPLICATED");
        var invalid = VehicleServiceTests.ValidRequest("INVALID", "NEW-01"); invalid.CapacityHorses = 12;
        var validation = await Error(await client.PostAsJsonAsync("/api/v1/vehicles", invalid), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal("capacityHorses", validation.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Contains("BR-BOOKING-02", validation.GetProperty("errors")[0].GetProperty("message").GetString());
        foreach (var payload in new[] {
            new { vehicleCode = "SYSTEM", licensePlate = "SYSTEM", capacityHorses = (object)6, status = (string?)"IN_USE" },
            new { vehicleCode = "FRACTION", licensePlate = "FRACTION", capacityHorses = (object)2.5, status = (string?)null } })
            await Error(await client.PostAsJsonAsync("/api/v1/vehicles", payload), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 403)]
    [InlineData(EmployeeRole.DRIVER, 403)]
    [InlineData(EmployeeRole.ESCORT, 403)]
    public async Task Http_AllFiveRoutes_EnforceRolePolicy(EmployeeRole role, int expected)
    {
        await using var db = TestSupport.CreateDb();
        var (account, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role; await db.SaveChangesAsync();
        var allowedAccount = await TestSupport.AddLogisticsManagerAsync(db);
        var vehicle = await VehicleServiceTests.CreateService(db).CreateAsync(VehicleServiceTests.ValidRequest(), allowedAccount.Account.AccountId);
        await using var host = await StartAsync(db);
        using var client = Client(host, account.AccountId, role);
        Assert.Equal(expected, (int)(await client.GetAsync("/api/v1/vehicles")).StatusCode);
        Assert.Equal(expected, (int)(await client.GetAsync($"/api/v1/vehicles/{vehicle.VehicleId}")).StatusCode);
        Assert.Equal(expected == 200 ? 201 : expected, (int)(await client.PostAsJsonAsync("/api/v1/vehicles", VehicleServiceTests.ValidRequest("NEXT", "NEXT-01"))).StatusCode);
        Assert.Equal(expected, (int)(await client.PutAsJsonAsync($"/api/v1/vehicles/{vehicle.VehicleId}", VehicleServiceTests.UpdateRequest(vehicle))).StatusCode);
        var detail = await VehicleServiceTests.CreateService(db).GetAsync(vehicle.VehicleId, allowedAccount.Account.AccountId);
        Assert.Equal(expected, (int)(await client.PatchAsJsonAsync($"/api/v1/vehicles/{vehicle.VehicleId}/status", VehicleServiceTests.StatusRequest(detail.UpdatedAt))).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/vehicles")).StatusCode);
        employee.IsActive = false; await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(host, account.AccountId, role));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/vehicles")).StatusCode);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "vehicle-tests", ["JwtOptions:Audience"] = "vehicle-tests" });
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<TimeProvider>(VehicleServiceTests.TestClock);
        builder.Services.AddControllers().AddApplicationPart(typeof(VehiclesController).Assembly).ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                context.ModelState.Where(x => x.Value!.Errors.Count > 0).SelectMany(x => x.Value!.Errors.Select(e => new { field = x.Key, message = e.ErrorMessage })),
                "The request data is invalid")));
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddScoped<Pegaxus.Service.VehicleService.IService, Pegaxus.Service.VehicleService.Service>();
        builder.Services.AddScoped<IValidator<Request.ListRequest>, ListRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.CreateRequest>, CreateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.UpdateRequest>, UpdateRequestValidator>();
        builder.Services.AddScoped<IValidator<Request.StatusRequest>, StatusRequestValidator>();
        builder.Services.AddScoped<Pegaxus.Service.NotificationService.IService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static string Token(WebApplication host, Guid id, EmployeeRole role) => new JwtServices(host.Configuration).GenerateAccessToken(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "Employee"), new Claim(ClaimTypes.Role, JwtServices.GetRoleName(role))]);
    private static HttpClient Client(WebApplication host, Guid id, EmployeeRole role)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(host, id, role));
        return client;
    }
    private static async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(code, body.GetProperty("errorCode").GetString());
        return body;
    }
}
