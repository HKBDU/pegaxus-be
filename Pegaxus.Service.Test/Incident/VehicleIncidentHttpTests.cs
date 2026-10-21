using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class VehicleIncidentHttpTests
{
    [Fact]
    public async Task CombinedIncident_HorseAndVehicleUpdatesShareVersionAndPreserveMedicalEvidence()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var driver = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        using var escort = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        using var coordinator = Client(host, f.Coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var request = f.Report(); request.Category = "BOTH"; request.VehicleOperable = false;
        request.ReplacementVehicleRequired = true; request.PhotoFiles = [Photo()];
        using var report = await driver.PostAsJsonAsync("/api/v1/incidents/vehicle", request);
        Assert.Equal(HttpStatusCode.Created, report.StatusCode);
        var created = (await report.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.True(created.GetProperty("effects").GetProperty("canAddHorseDetails").GetBoolean());
        Assert.False(created.GetProperty("effects").TryGetProperty("healthLogsCreated", out _));
        var id = created.GetProperty("incidentReportId").GetGuid();
        var horseId = await db.BookingHorses.Where(item => item.BookingId == f.Transport.BookingId).Select(item => item.HorseId).FirstAsync();
        using var addition = await escort.PostAsJsonAsync($"/api/v1/incidents/{id}/horse-details", new
        {
            horses = new[] { new { horseId, incidentType = "COLIC", conditionDescription = "Sweating and looking at the belly",
                bodyTemperatureCelsius = 39.2m, firstAidProvided = "Walk the horse", veterinarianRequired = true } },
            photoFiles = new[] { Photo() }
        });
        Assert.Equal(HttpStatusCode.Created, addition.StatusCode);
        var added = (await addition.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("CRITICAL", added.GetProperty("severity").GetString());
        Assert.Equal(created.GetProperty("vehicleDetail").GetRawText(), added.GetProperty("vehicleDetail").GetRawText());
        Assert.Equal(1, added.GetProperty("effects").GetProperty("healthLogsCreated").GetInt32());
        Assert.False(added.GetProperty("effects").TryGetProperty("vehicleStatusChanged", out _));
        var detail = Assert.Single(added.GetProperty("horseDetails").EnumerateArray());
        var followUpPath = $"/api/v1/incidents/{id}/horse-details/{detail.GetProperty("horseHealthIncidentDetailId").GetGuid()}";
        using var followUp = await escort.PatchAsJsonAsync(followUpPath, new
        {
            lastUpdatedAt = added.GetProperty("updatedAt").GetString(), outcomeNote = "Veterinarian assessed the horse",
            medicationGiven = "Vet prescribed treatment", horseTransportable = false
        });
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        var followed = (await followUp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        using var update = await coordinator.PatchAsJsonAsync($"/api/v1/incidents/{id}/vehicle-detail", new
        {
            lastUpdatedAt = followed.GetProperty("updatedAt").GetString(), vehicleOperable = true,
            repairNote = "Mechanic finished repairs", photoFiles = new[] { Photo() }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(followed.GetProperty("horseDetails").GetRawText(), updated.GetProperty("horseDetails").GetRawText());
        Assert.Equal("OUT_OF_SERVICE", updated.GetProperty("vehicleDetail").GetProperty("vehicle").GetProperty("status").GetString());
        Assert.Equal(3, updated.GetProperty("photos").GetArrayLength());
        var medicalLog = await db.HorseHealthLogs.SingleAsync();
        Assert.Equal(HorseHealthStatus.CRITICAL, medicalLog.HealthStatus); Assert.Equal(39.2m, medicalLog.TemperatureCelsius);
        Assert.Equal("Walk the horse", medicalLog.TreatmentProvided); Assert.Null(medicalLog.MedicationGiven); Assert.Single(medicalLog.PhotoUrls);
        Assert.Equal(2, await db.IncidentPhotos.CountAsync(item => item.VehicleIncidentDetailId != null));
        Assert.Equal(1, await db.AccountNotifications.CountAsync(item => item.AccountId == f.CustomerAccountId));
        using var stale = await escort.PatchAsJsonAsync(followUpPath, new { lastUpdatedAt = followed.GetProperty("updatedAt").GetString(), outcomeNote = "Stale follow-up" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("INCIDENT_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        using var replay = await driver.PostAsJsonAsync("/api/v1/incidents/vehicle", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(3, f.Storage.Confirmations.Count);
        var effects = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("effects");
        Assert.All(effects.EnumerateObject(), effect => Assert.False(effect.Value.GetBoolean()));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Json_ReportReplayPatchAndContacts_UseEnvelopeAndContractFields()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        var body = JsonSerializer.SerializeToNode(f.Report(), JsonOptions)!.AsObject(); body.Remove("category"); body["currency"] = "USD";
        body["photoFiles"] = JsonSerializer.SerializeToNode(new[] { Photo() }, JsonOptions); body["estimatedRepairCost"] = 1000;
        using var created = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); var envelope = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(envelope.GetProperty("isSuccess").GetBoolean()); Assert.Equal(201, envelope.GetProperty("code").GetInt32());
        var value = envelope.GetProperty("value"); var id = value.GetProperty("incidentReportId").GetGuid();
        Assert.Equal("VEHICLE", value.GetProperty("category").GetString()); Assert.Equal("VND", value.GetProperty("costSummary").GetProperty("currency").GetString());
        Assert.Equal("1000", value.GetProperty("vehicleDetail").GetProperty("estimatedRepairCost").GetRawText());
        Assert.Equal(1, value.GetProperty("photoCount").GetInt32()); Assert.Equal(Storage.SignedUrl, value.GetProperty("photos")[0].GetProperty("photoUrl").GetString());
        Assert.DoesNotContain((await db.IncidentPhotos.SingleAsync()).PhotoUrl, value.GetRawText());
        Assert.EndsWith("+00:00", value.GetProperty("updatedAt").GetString());
        using var replay = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayValue = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value"); Assert.Equal(id, replayValue.GetProperty("incidentReportId").GetGuid());
        foreach (var effect in replayValue.GetProperty("effects").EnumerateObject()) Assert.Equal(JsonValueKind.False, effect.Value.ValueKind);
        using var contacts = await client.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/emergency-contacts");
        Assert.Equal(HttpStatusCode.OK, contacts.StatusCode); var contactValue = (await contacts.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(4, contactValue.GetProperty("contacts").GetArrayLength()); Assert.True(contactValue.TryGetProperty("serverTime", out _));
        Assert.Equal(value.GetProperty("safety").GetProperty("emergencyContacts").GetRawText(), contactValue.GetProperty("contacts").GetRawText());
        using var updated = await client.PatchAsJsonAsync($"/api/v1/incidents/{id}/vehicle-detail", new
            { lastUpdatedAt = value.GetProperty("updatedAt").GetString(), repairNote = "Mechanic arrived", estimatedRepairHours = 2.5 });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode); var updateValue = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("Mechanic arrived", updateValue.GetProperty("vehicleDetail").GetProperty("repairNote").GetString());
        Assert.Equal(2.5m, updateValue.GetProperty("vehicleDetail").GetProperty("estimatedRepairHours").GetDecimal());
        Assert.False(updateValue.TryGetProperty("effects", out _)); Assert.Single(f.Storage.Confirmations);
        using var stale = await client.PatchAsJsonAsync($"/api/v1/incidents/{id}/vehicle-detail", new { lastUpdatedAt = value.GetProperty("updatedAt").GetString(), repairRequired = true });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("INCIDENT_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("transportId", "invalid")][InlineData("occurredAt", "invalid")]
    [InlineData("vehicleOperable", null)][InlineData("vehicleOperable", "false")]
    [InlineData("description", "  short ")][InlineData("severity", "UNKNOWN")]
    [InlineData("estimatedRepairCost", "1000.0")][InlineData("estimatedRepairCost", "1000.1")]
    [InlineData("estimatedRepairHours", "1.25")][InlineData("photoFiles", "null")]
    public async Task Json_RejectsInvalidFieldsAndLexicalDecimalVnd(string field, string? input)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        var body = JsonSerializer.SerializeToNode(f.Report(), JsonOptions)!.AsObject();
        if (input is null) body.Remove(field);
        else if (field is "estimatedRepairCost" or "estimatedRepairHours" or "photoFiles") body[field] = JsonNode.Parse(input);
        else body[field] = input;
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/incidents/vehicle", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(error.GetProperty("isSuccess").GetBoolean()); Assert.Equal("VALIDATION_ERROR", error.GetProperty("errorCode").GetString());
        Assert.NotEmpty(error.GetProperty("errors").EnumerateArray());
        Assert.Empty(db.IncidentReports); Assert.Empty(f.Storage.Confirmations);
    }

    [Fact]
    public async Task Json_TooManyImagesHasDedicatedError_AndCustomerCannotAccessIncidentWritesOrContacts()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var value = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value;
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        var request = f.Report(); request.PhotoFiles = Enumerable.Range(0, 6).Select(_ => Photo()).ToList();
        using var tooMany = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", request);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("TOO_MANY_IMAGES", (await tooMany.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, f.CustomerAccountId.ToString()), new Claim(ClaimTypes.Role, JwtServices.GetRoleName(AccountType.CUSTOMER))]));
        using var report = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", f.Report()); Assert.Equal(HttpStatusCode.Forbidden, report.StatusCode);
        using var contacts = await client.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/emergency-contacts"); Assert.Equal(HttpStatusCode.Forbidden, contacts.StatusCode);
        using var patch = await client.PatchAsJsonAsync($"/api/v1/incidents/{value.IncidentReportId}/vehicle-detail", new { lastUpdatedAt = value.UpdatedAt, repairRequired = true });
        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode); Assert.Empty(f.Storage.Confirmations);
    }

    [Fact]
    public async Task Json_PatchRequiresVersionAndNonPhotoField_AndWriteEndpointsRejectMultipart()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var value = (await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Value;
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        var url = $"/api/v1/incidents/{value.IncidentReportId}/vehicle-detail";
        foreach (var body in new object[] { new { }, new { repairNote = "Missing version" }, new { lastUpdatedAt = value.UpdatedAt, photoFiles = new[] { Photo() } } })
        {
            using var response = await client.PatchAsJsonAsync(url, body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        }
        foreach (var endpoint in new[] { "/api/v1/incidents/vehicle", url })
        {
            using var form = new MultipartFormDataContent(); form.Add(new StringContent("test"), "title");
            using var request = new HttpRequestMessage(endpoint == url ? HttpMethod.Patch : HttpMethod.Post, endpoint) { Content = form };
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
        Assert.Empty(f.Storage.Confirmations);
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, false, true)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, false, true)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, false, false)]
    [InlineData(EmployeeRole.DRIVER, true, true)]
    [InlineData(EmployeeRole.ESCORT, true, true)]
    public async Task Authorization_ChecksTokenRoleAndLiveDatabaseScope(EmployeeRole role, bool canReport, bool canReadContacts)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var actor = role switch { EmployeeRole.LOGISTICS_MANAGER => f.Manager, EmployeeRole.FLEET_ROUTE_COORDINATOR => f.Coordinator,
            EmployeeRole.TRANSPORT_SPECIALIST => f.Specialist, EmployeeRole.ESCORT => f.Escort, _ => f.Driver };
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, actor.AccountId, role);
        using var report = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", f.Report());
        Assert.Equal(canReport ? HttpStatusCode.Created : HttpStatusCode.Forbidden, report.StatusCode);
        using var contacts = await client.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/emergency-contacts");
        Assert.Equal(canReadContacts ? HttpStatusCode.OK : HttpStatusCode.Forbidden, contacts.StatusCode);
        using var forged = Client(host, f.Specialist.AccountId, EmployeeRole.DRIVER);
        using var forgedReport = await forged.PostAsJsonAsync("/api/v1/incidents/vehicle", f.Report()); Assert.Equal(HttpStatusCode.Forbidden, forgedReport.StatusCode);
        using var forgedContacts = await forged.GetAsync($"/api/v1/transports/{f.Transport.TransportId}/emergency-contacts"); Assert.Equal(HttpStatusCode.Forbidden, forgedContacts.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PostAsJsonAsync("/api/v1/incidents/vehicle", f.Report()); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, Storage storage)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "incident-tests", ["JwtOptions:Audience"] = "incident-tests" });
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<TimeProvider>(new FixedClock());
        builder.Services.AddApiControllers(); builder.Services.AddControllers().AddApplicationPart(typeof(IncidentsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration); builder.Services.AddTransportPlanningModule();
        builder.Services.AddSingleton<IMediaService>(storage); builder.Services.AddSingleton<IDocumentStorageService>(storage);
        builder.Services.AddScoped<INotificationService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>(); builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
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
