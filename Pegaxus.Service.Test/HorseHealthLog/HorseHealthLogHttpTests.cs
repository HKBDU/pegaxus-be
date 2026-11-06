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

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class HorseHealthLogHttpTests
{
    [Fact]
    public async Task Json_CreateReturnsFullCheckAndReplayHasNoRepeatedEffects()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        var url = $"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs";
        using var form = Form(f, fields: new() { ["healthStatus"] = "CRITICAL", ["temperatureCelsius"] = "38.2",
            ["heartRateBpm"] = "38", ["respiratoryRateBpm"] = "14", ["veterinarianRequired"] = "true",
            ["veterinarianNote"] = "Call vet", ["generalNote"] = "Scratch", ["latitude"] = "ignored",
            ["longitude"] = "ignored", ["photoLatitudes"] = "ignored", ["photoCapturedAts"] = "ignored", ["photoCaptions"] = "ignored" }, photo: true);
        using var response = await client.PostAsync(url, form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(201, body.GetProperty("code").GetInt32());
        Assert.Equal($"Horse health log for {f.Horse.HorseName} saved successfully", body.GetProperty("message").GetString());
        var value = body.GetProperty("value");
        Assert.Equal(f.Transport.TransportId, value.GetProperty("transportId").GetGuid());
        Assert.Equal(f.Horse.HorseId, value.GetProperty("horse").GetProperty("horseId").GetGuid());
        Assert.Equal(f.Horse.PassportNumber, value.GetProperty("horse").GetProperty("passportNumber").GetString());
        Assert.Equal(f.Escort.EmployeeId, value.GetProperty("checkedBy").GetProperty("employeeId").GetGuid());
        Assert.Equal(38.2m, value.GetProperty("temperatureCelsius").GetDecimal());
        Assert.Equal("Call vet", value.GetProperty("veterinarianNote").GetString());
        Assert.Equal("Scratch", value.GetProperty("generalNote").GetString());
        Assert.True(value.GetProperty("effects").GetProperty("emergencyNotified").GetBoolean());
        Assert.True(value.GetProperty("effects").GetProperty("canReportIncident").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("gpsCoordinates").ValueKind);
        var photo = Assert.Single(value.GetProperty("photos").EnumerateArray());
        Assert.Equal(Storage.SignedUrl, photo.GetProperty("photoUrl").GetString());
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("caption").ValueKind);
        foreach (var field in new[] { "behaviorNote", "symptomNote", "injuryNote", "feedingNote", "hydrationNote",
            "treatmentProvided", "medicationGiven", "locationDescription", "createdAt", "updatedAt", "hasPhoto" })
            Assert.True(value.TryGetProperty(field, out _), field);
        using var replayForm = Form(f, fields: new() { ["generalNote"] = "Must not overwrite" }, photo: true);
        using var replay = await client.PostAsync(url, replayForm);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayValue = (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(value.GetProperty("horseHealthLogId").GetGuid(), replayValue.GetProperty("horseHealthLogId").GetGuid());
        Assert.Equal("CRITICAL", replayValue.GetProperty("healthStatus").GetString());
        Assert.False(replayValue.GetProperty("effects").GetProperty("emergencyNotified").GetBoolean());
        Assert.False(replayValue.GetProperty("effects").GetProperty("canReportIncident").GetBoolean());
        Assert.Single(f.Storage.Confirmations); Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Empty(db.IncidentReports);
    }

    [Fact]
    public async Task Json_BatchReturnsSummariesAndSharedPhotoWithoutAlerts()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        var url = $"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs/mark-all-normal";
        foreach (var replay in new[] { false, true })
        {
            using var form = Form(f, batch: true, photo: true);
            using var response = await client.PostAsync(url, form);
            Assert.Equal(replay ? HttpStatusCode.OK : HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Health logs for all 2 horses saved successfully", body.GetProperty("message").GetString());
            var value = body.GetProperty("value");
            Assert.Equal(f.Transport.TransportId, value.GetProperty("transportId").GetGuid());
            Assert.Equal(replay ? 0 : 2, value.GetProperty("createdCount").GetInt32());
            Assert.Equal(2, value.GetProperty("logs").GetArrayLength());
            foreach (var log in value.GetProperty("logs").EnumerateArray())
            {
                Assert.Equal("NORMAL", log.GetProperty("healthStatus").GetString());
                Assert.Equal("NORMAL", log.GetProperty("hydrationStatus").GetString());
                Assert.True(log.GetProperty("hasPhoto").GetBoolean());
                Assert.False(log.TryGetProperty("photos", out _)); Assert.False(log.TryGetProperty("effects", out _));
            }
        }
        Assert.Single(f.Storage.Confirmations); Assert.Equal(2, await db.HorseHealthLogs.CountAsync());
        Assert.Single((await db.HorseHealthLogs.ToListAsync()).SelectMany(log => log.PhotoUrls).Distinct());
        Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("horseId", "not-a-uuid", 400, "VALIDATION_ERROR")]
    [InlineData("logType", "", 400, "VALIDATION_ERROR")]
    [InlineData("logType", "1", 400, "VALIDATION_ERROR")]
    [InlineData("healthStatus", "UNKNOWN", 400, "VALIDATION_ERROR")]
    [InlineData("hydrationStatus", "", 400, "VALIDATION_ERROR")]
    [InlineData("checkedAt", "invalid", 400, "VALIDATION_ERROR")]
    [InlineData("heartRateBpm", "38.2", 400, "VALIDATION_ERROR")]
    [InlineData("heartRateBpm", "201", 422, "VITAL_SIGNS_OUT_OF_RANGE")]
    [InlineData("temperatureCelsius", "38.25", 400, "VALIDATION_ERROR")]
    [InlineData("veterinarianRequired", "true", 400, "VALIDATION_ERROR")]
    [InlineData("veterinarianNote", "", 400, "VALIDATION_ERROR")]
    [InlineData("veterinarianNote", "unexpected", 400, "VALIDATION_ERROR")]
    public async Task Json_ValidationUsesContractErrors(string field, string input, int status, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        using var form = Form(f, fields: new() { [field] = input });
        using var response = await client.PostAsync($"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs", form);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Empty(db.HorseHealthLogs); Assert.Empty(f.Storage.Confirmations);
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Authorization_RequiresTokenDatabaseRoleAndLiveAssignment(EmployeeRole tokenRole)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, tokenRole);
        var url = $"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs";
        using var form = Form(f);
        using var response = await client.PostAsync(url, form);
        Assert.Equal(tokenRole == EmployeeRole.ESCORT ? HttpStatusCode.Created : HttpStatusCode.Forbidden, response.StatusCode);
        using var forged = Client(host, f.Manager.AccountId, EmployeeRole.ESCORT);
        using var forgedForm = Form(f);
        using var denied = await forged.PostAsync(url, forgedForm);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("Only the assigned escort can record horse health logs",
            (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymousForm = Form(f);
        using var anonymous = await client.PostAsync(url, anonymousForm);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Json_AppendReturnsAllSignedPhotosAndIgnoresUnsupportedMetadataAndCheckEdits()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = f.Request(); request.Files = [Photo()]; request.GeneralNote = "Original";
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.HorseHealthLogId;
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        using var form = PhotosForm(2);
        using var response = await client.PostAsync($"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs/{id}/photos", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, body.GetProperty("code").GetInt32());
        Assert.Equal("Horse health log photos added successfully", body.GetProperty("message").GetString());
        var photos = body.GetProperty("value").GetProperty("photos"); Assert.Equal(3, photos.GetArrayLength());
        foreach (var photo in photos.EnumerateArray())
        {
            Assert.Equal(Storage.SignedUrl, photo.GetProperty("photoUrl").GetString());
            foreach (var field in new[] { "caption", "latitude", "longitude", "capturedAt" })
                Assert.Equal(JsonValueKind.Null, photo.GetProperty(field).ValueKind);
        }
        using var read = await client.GetAsync($"/api/v1/horse-health-logs/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var detail = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal(3, detail.GetProperty("photos").GetArrayLength()); Assert.Equal("Original", detail.GetProperty("generalNote").GetString());
        Assert.Equal("NORMAL", detail.GetProperty("healthStatus").GetString());
        foreach (var url in (await db.HorseHealthLogs.SingleAsync()).PhotoUrls) Assert.DoesNotContain(url, detail.GetRawText());
    }

    [Theory]
    [InlineData(0, 400, "VALIDATION_ERROR")]
    [InlineData(6, 400, "VALIDATION_ERROR")]
    [InlineData(5, 422, "PHOTO_LIMIT_EXCEEDED")]
    public async Task Json_AppendEnforcesPerRequestAndTotalPhotoLimits(int count, int status, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var request = f.Request(); request.Files = [Photo()];
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, request, f.Escort.AccountId)).Value.HorseHealthLogId;
        await using var host = await StartAsync(db, f.Storage);
        using var client = Client(host, f.Escort.AccountId, EmployeeRole.ESCORT);
        using var form = PhotosForm(count);
        using var response = await client.PostAsync($"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs/{id}/photos", form);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Single(f.Storage.Confirmations); Assert.Single((await db.HorseHealthLogs.SingleAsync()).PhotoUrls);
    }

    [Fact]
    public async Task Json_AppendRequiresAuthenticationRoleAuthorAndLiveScope()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var id = (await f.Service.CreateAsync(f.Transport.TransportId, f.Request(), f.Escort.AccountId)).Value.HorseHealthLogId;
        var other = await EmployeeAsync(db, EmployeeRole.ESCORT);
        await using var host = await StartAsync(db, f.Storage);
        var url = $"/api/v1/transports/{f.Transport.TransportId}/horse-health-logs/{id}/photos";
        foreach (var (accountId, role) in new[] { (f.Escort.AccountId, EmployeeRole.DRIVER), (f.Manager.AccountId, EmployeeRole.ESCORT),
            (other.AccountId, EmployeeRole.ESCORT) })
        {
            using var client = Client(host, accountId, role); using var form = PhotosForm(1);
            using var response = await client.PostAsync(url, form); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        f.Segment.EscortEmployeeId = other.EmployeeId; await db.SaveChangesAsync();
        using var assignedOther = Client(host, other.AccountId, EmployeeRole.ESCORT);
        using var otherForm = PhotosForm(1); using var authorDenied = await assignedOther.PostAsync(url, otherForm);
        Assert.Equal(HttpStatusCode.Forbidden, authorDenied.StatusCode);
        assignedOther.DefaultRequestHeaders.Authorization = null;
        using var anonymousForm = PhotosForm(1); using var anonymous = await assignedOther.PostAsync(url, anonymousForm);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); Assert.Empty(f.Storage.Confirmations);
    }

    private static JsonContent PhotosForm(int count) => JsonContent.Create(new { files = Enumerable.Range(0, count).Select(_ => Photo()).ToArray() });

    private static JsonContent Form(Fixture f, bool batch = false, Dictionary<string, string>? fields = null, bool photo = false)
    {
        var values = new Dictionary<string, object?> { ["logType"] = "ROUTINE_CHECK", ["checkedAt"] = Fixture.CheckTime.ToString("O") };
        if (!batch) { values["horseId"] = f.Horse.HorseId; values["healthStatus"] = "NORMAL"; values["hydrationStatus"] = "NORMAL"; }
        if (fields is not null) foreach (var pair in fields)
            values[pair.Key] = pair.Key == "veterinarianRequired" && bool.TryParse(pair.Value, out var boolean) ? boolean :
                new[] { "heartRateBpm", "respiratoryRateBpm", "temperatureCelsius" }.Contains(pair.Key) && decimal.TryParse(pair.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : pair.Value;
        if (photo) values["files"] = new[] { Photo() };
        return JsonContent.Create(values);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, Storage storage)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "health-log-tests", ["JwtOptions:Audience"] = "health-log-tests" });
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<TimeProvider>(new FixedClock());
        builder.Services.AddApiControllers(); builder.Services.AddControllers().AddApplicationPart(typeof(TransportHorseHealthLogsController).Assembly);
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
