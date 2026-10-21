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
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.HorseIncidentTests;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class HorseIncidentHttpTests
{
    [Fact]
    public async Task Json_ReportReplayAdditionAndFollowUpReturnContractAndPreserveNullableState()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        var request = f.Report(); request.PhotoFiles = [Photo()]; request.Horses[0].BodyTemperatureCelsius = 39.8m;
        using var response = await client.PostAsJsonAsync("/api/v1/incidents/horse-health", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); var incident = body.GetProperty("value");
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(201, body.GetProperty("code").GetInt32());
        Assert.Equal("Horse health incident " + incident.GetProperty("incidentCode").GetString() + " reported successfully", body.GetProperty("message").GetString());
        var horse = Assert.Single(incident.GetProperty("horseDetails").EnumerateArray());
        Assert.Equal("BODY_TEMPERATURE_OUTSIDE_NORMAL", Assert.Single(horse.GetProperty("vitalWarnings").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, horse.GetProperty("horseTransportable").ValueKind);
        Assert.Equal(Storage.SignedUrl, Assert.Single(incident.GetProperty("photos").EnumerateArray()).GetProperty("photoUrl").GetString());
        Assert.Equal("URGENT", incident.GetProperty("effects").GetProperty("notifiedPriority").GetString());
        Assert.Equal(1, incident.GetProperty("effects").GetProperty("healthLogsCreated").GetInt32());
        Assert.False(incident.GetProperty("effects").TryGetProperty("vehicleStatusChanged", out _));
        Assert.False(incident.GetProperty("effects").TryGetProperty("canAddHorseDetails", out _));
        Assert.Equal("COLIC", Assert.Single(incident.GetProperty("effects").GetProperty("firstAidGuide").EnumerateArray()).GetString());
        Assert.Equal(4, incident.GetProperty("safety").GetProperty("emergencyContacts").GetArrayLength());
        foreach (var field in new[] { "transport", "location", "reportedBy", "description", "costSummary", "routeChanges", "availableActions", "canResolve", "serverTime", "updatedAt" })
            Assert.True(incident.TryGetProperty(field, out _), field);
        using var replay = await client.PostAsJsonAsync("/api/v1/incidents/horse-health", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayEffects = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("effects");
        Assert.False(replayEffects.GetProperty("notifiedPriority").GetBoolean()); Assert.False(replayEffects.GetProperty("emailQueued").GetBoolean());
        Assert.Equal(0, replayEffects.GetProperty("healthLogsCreated").GetInt32()); Assert.Single(f.Storage.Confirmations);
        var id = incident.GetProperty("incidentReportId").GetGuid(); var detailId = horse.GetProperty("horseHealthIncidentDetailId").GetGuid();
        using var addedResponse = await client.PostAsJsonAsync($"/api/v1/incidents/{id}/horse-details", new { horses = new[] { f.HorseRequest(f.SecondHorseId) } });
        Assert.Equal(HttpStatusCode.Created, addedResponse.StatusCode);
        var added = (await addedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value"); Assert.Equal(2, added.GetProperty("horseDetails").GetArrayLength());
        var path = $"/api/v1/incidents/{id}/horse-details/{detailId}"; var version = added.GetProperty("updatedAt").GetString();
        foreach (var fields in new Dictionary<string, object?>[]
        {
            new() { ["horseTransportable"] = false }, new() { ["outcomeNote"] = "Vet examination complete" }, new() { ["horseTransportable"] = null }
        })
        {
            fields["lastUpdatedAt"] = version; using var patch = await client.PatchAsJsonAsync(path, fields);
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode); var updated = (await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
            var updatedHorse = updated.GetProperty("horseDetails").EnumerateArray().Single(item => item.GetProperty("horseHealthIncidentDetailId").GetGuid() == detailId);
            if (fields.TryGetValue("horseTransportable", out var state) && state is null) Assert.Equal(JsonValueKind.Null, updatedHorse.GetProperty("horseTransportable").ValueKind);
            else Assert.False(updatedHorse.GetProperty("horseTransportable").GetBoolean());
            Assert.False(updatedHorse.TryGetProperty("vitalWarnings", out _)); Assert.False(updated.TryGetProperty("effects", out _));
            version = updated.GetProperty("updatedAt").GetString();
        }
        using var stale = await client.PatchAsJsonAsync(path, new { lastUpdatedAt = added.GetProperty("updatedAt").GetString(), outcomeNote = "Stale" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("INCIDENT_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(2, await db.HorseHealthLogs.CountAsync()); Assert.Equal(2, await db.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("bodyTemperatureCelsius")][InlineData("heartRateBpm")][InlineData("respiratoryRateBpm")][InlineData("symptoms")]
    [InlineData("conditionDescription")][InlineData("incidentType")][InlineData("firstAidProvided")][InlineData("veterinarianRequired")]
    [InlineData("BODYTEMPERATURECELSIUS")]
    public async Task FollowUp_RejectsEveryImmutableMedicalFieldEvenWhenNull(string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var incident = (await f.Service.ReportHorseHealthAsync(f.Report(), f.Escort.AccountId)).Value;
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        var patch = new Dictionary<string, object?> { ["lastUpdatedAt"] = incident.UpdatedAt, ["outcomeNote"] = "Attempted edit", [field] = null };
        using var response = await client.PatchAsJsonAsync($"/api/v1/incidents/{incident.IncidentReportId}/horse-details/{incident.HorseDetails[0].HorseHealthIncidentDetailId}", patch);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("IMMUTABLE_MEDICAL_RECORD", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Null((await db.HorseHealthIncidentDetails.SingleAsync()).OutcomeNote);
    }

    [Theory]
    [InlineData("temperature", 400, "VALIDATION_ERROR")][InlineData("vitals", 422, "VITAL_SIGNS_OUT_OF_RANGE")]
    [InlineData("empty", 400, "VALIDATION_ERROR")][InlineData("location", 400, "VALIDATION_ERROR")]
    [InlineData("photos", 400, "TOO_MANY_IMAGES")][InlineData("horse", 422, "HORSE_NOT_IN_TRANSPORT")]
    [InlineData("missingLocation", 404, "LOCATION_NOT_FOUND")]
    public async Task Report_UsesContractErrorCodesAndFieldEnvelope(string scenario, int status, string errorCode)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        var request = f.Report();
        switch (scenario)
        {
            case "temperature": request.Horses[0].BodyTemperatureCelsius = 38.25m; break;
            case "vitals": request.Horses[0].BodyTemperatureCelsius = 34.9m; break;
            case "empty": request.Horses = []; break;
            case "location": request.LocationId = null; break;
            case "photos": request.PhotoFiles = Enumerable.Range(0, 6).Select(_ => Photo()).ToList(); break;
            case "horse": request.Horses[0].HorseId = Guid.NewGuid(); break;
            case "missingLocation": request.LocationId = Guid.NewGuid(); break;
        }
        using var response = await client.PostAsJsonAsync("/api/v1/incidents/horse-health", request);
        Assert.Equal(status, (int)response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(errorCode, body.GetProperty("errorCode").GetString());
        if (errorCode == "VALIDATION_ERROR") Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        Assert.Empty(db.IncidentReports); Assert.Empty(db.HorseHealthLogs); Assert.Empty(f.Storage.Confirmations);
    }

    [Fact]
    public async Task Authorization_EnforcesTokenRoleDatabaseRoleAssignmentAndJsonOnly()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        foreach (var role in new[] { EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR, EmployeeRole.TRANSPORT_SPECIALIST })
        {
            using var wrongRole = Client(host, f.Escort.AccountId, role);
            using var denied = await wrongRole.PostAsJsonAsync("/api/v1/incidents/horse-health", f.Report()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var forged = Client(host, f.Manager.AccountId, EmployeeRole.ESCORT);
        using var forgedResult = await forged.PostAsJsonAsync("/api/v1/incidents/horse-health", f.Report()); Assert.Equal(HttpStatusCode.Forbidden, forgedResult.StatusCode);
        var outsider = await EmployeeAsync(db, EmployeeRole.ESCORT); using var unassigned = Client(host, outsider.AccountId, EmployeeRole.ESCORT);
        using var unassignedResult = await unassigned.PostAsJsonAsync("/api/v1/incidents/horse-health", f.Report()); Assert.Equal(HttpStatusCode.Forbidden, unassignedResult.StatusCode);
        using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        using var multipart = await client.PostAsync("/api/v1/incidents/horse-health", new MultipartFormDataContent()); Assert.Equal(HttpStatusCode.UnsupportedMediaType, multipart.StatusCode);
        var created = (await f.Service.ReportHorseHealthAsync(f.Report(), f.Driver.AccountId)).Value;
        using var addDenied = await client.PostAsJsonAsync($"/api/v1/incidents/{created.IncidentReportId}/horse-details", new { horses = new[] { f.HorseRequest(f.SecondHorseId) } });
        Assert.Equal(HttpStatusCode.Forbidden, addDenied.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PostAsJsonAsync("/api/v1/incidents/horse-health", f.Report()); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, Storage storage)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "incident-tests", ["JwtOptions:Audience"] = "incident-tests" });
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<TimeProvider>(new FixedClock());
        builder.Services.AddApiControllers(); builder.Services.AddControllers().AddApplicationPart(typeof(IncidentsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration); builder.Services.AddTransportPlanningModule();
        builder.Services.AddSingleton<IMediaService>(storage); builder.Services.AddSingleton<IDocumentStorageService>(storage);
        builder.Services.AddScoped<INotificationService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllers(); await app.StartAsync(); return app;
    }

    private static HttpClient Client(WebApplication host, Guid accountId, EmployeeRole role)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, "Employee"), new Claim(ClaimTypes.Role, JwtServices.GetRoleName(role))]));
        return client;
    }
}
