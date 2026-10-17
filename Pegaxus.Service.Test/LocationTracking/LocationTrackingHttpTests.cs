using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Hubs;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Test.LocationTrackingTests;

using Claim = System.Security.Claims.Claim;
using Tracking = Pegaxus.Service.LocationTrackingService;

namespace Pegaxus.Service.Test;

public class LocationTrackingHttpTests
{
    [Fact]
    public async Task Http_GpsSnapshotAndPermissionDeniedUseContractEnvelopes()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f);
        using var driver = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        using var viewer = Client(host, f.CustomerAccountId);
        var url = $"/api/v1/transports/{f.TransportId}";
        var before = await viewer.GetFromJsonAsync<JsonElement>(url + "/tracking");
        Assert.Equal("Transport tracking retrieved successfully", before.GetProperty("message").GetString());
        var snapshot = before.GetProperty("value");
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("truck").ValueKind);
        Assert.Equal(f.TransportId, snapshot.GetProperty("transportId").GetGuid());
        Assert.Equal(2, snapshot.GetProperty("plannedRoute").GetProperty("segments").GetArrayLength());
        Assert.Equal(30, snapshot.GetProperty("plannedRemainingDistanceKm").GetDecimal());
        Assert.False(snapshot.TryGetProperty("breadcrumb", out _)); Assert.False(snapshot.TryGetProperty("gpsSignal", out _));
        using var accepted = await driver.PostAsJsonAsync(url + "/locations", f.Point(0.5m));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Location accepted", body.GetProperty("message").GetString());
        Assert.Equal(f.Clock.Now, body.GetProperty("value").GetProperty("serverTime").GetDateTimeOffset());
        var after = await viewer.GetFromJsonAsync<JsonElement>(url + "/tracking");
        Assert.Equal(0.5m, after.GetProperty("value").GetProperty("truck").GetProperty("longitude").GetDecimal());
        using var notified = await driver.PostAsync(url + "/locations/permission-denied", null);
        Assert.True((await notified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("notified").GetBoolean());
        using var repeated = await driver.PostAsync(url + "/locations/permission-denied", null);
        Assert.False((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("notified").GetBoolean());
        Assert.Single(await db.Notifications.ToListAsync());
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, 200, 403)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, 200, 403)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, 200, 403)]
    [InlineData(EmployeeRole.DRIVER, 403, 200)]
    [InlineData(EmployeeRole.ESCORT, 403, 403)]
    public async Task Http_AndSignalRPoliciesRejectUnapprovedRolesAndForgedClaims(EmployeeRole role, int read, int write)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var actor = await PersonnelAssignmentTests.EmployeeAsync(db, role);
        if (role == EmployeeRole.DRIVER) { f.First.DriverEmployeeId = actor.EmployeeId; await db.SaveChangesAsync(); }
        await using var host = await StartAsync(db, f);
        using var client = Client(host, actor.AccountId, role);
        var url = $"/api/v1/transports/{f.TransportId}";
        Assert.Equal(read, (int)(await client.GetAsync(url + "/tracking")).StatusCode);
        Assert.Equal(write, (int)(await client.PostAsJsonAsync(url + "/locations", f.Point(0.5m))).StatusCode);
        Assert.Equal(write, (int)(await client.PostAsync(url + "/locations/permission-denied", null)).StatusCode);
        Assert.Equal(read == 200 ? 200 : 403, (int)(await client.PostAsync("/hubs/transport-tracking/negotiate?negotiateVersion=1", null)).StatusCode);
        using var forged = Client(host, f.Manager.AccountId, EmployeeRole.DRIVER);
        Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync(url + "/locations", f.Point(0.5m))).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url + "/tracking")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"latitude\":91,\"longitude\":0,\"recordedAt\":\"2026-10-16T12:00:00Z\"}")]
    [InlineData("{\"latitude\":0,\"longitude\":-181,\"recordedAt\":\"2026-10-16T12:00:00Z\"}")]
    [InlineData("{\"latitude\":\"NaN\",\"longitude\":0,\"recordedAt\":\"2026-10-16T12:00:00Z\"}")]
    [InlineData("{\"latitude\":0,\"longitude\":0,\"recordedAt\":\"not-a-time\"}")]
    public async Task Http_InvalidJsonAndCoordinatesReturnValidationEnvelope(string json)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); await using var host = await StartAsync(db, f);
        using var client = Client(host, f.Driver.AccountId, EmployeeRole.DRIVER);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"/api/v1/transports/{f.TransportId}/locations", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Null(f.State.For(f.TransportId).Latest);
    }

    [Fact]
    public async Task WebSocket_RechecksDatabaseScopeAndStreamsOnlyJoinedTransportsWithExactPayload()
    {
        await using var db = TestSupport.CreateDb(); var first = await Fixture.CreateAsync(db); var second = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, first);
        await using var owner = await Connection.OpenAsync(host, first.CustomerAccountId);
        await using var manager = await Connection.OpenAsync(host, first.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        await using var stranger = await Connection.OpenAsync(host, second.CustomerAccountId);
        await using var forged = await Connection.OpenAsync(host, first.Driver.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        Assert.False((await owner.InvokeAsync("JoinTransport", first.TransportId)).TryGetProperty("error", out _));
        Assert.False((await manager.InvokeAsync("JoinTransport", first.TransportId)).TryGetProperty("error", out _));
        Assert.False((await stranger.InvokeAsync("JoinTransport", second.TransportId)).TryGetProperty("error", out _));
        Assert.Contains("TRANSPORT_NOT_FOUND", (await owner.InvokeAsync("JoinTransport", second.TransportId)).GetProperty("error").GetString());
        Assert.Contains("FORBIDDEN", (await forged.InvokeAsync("JoinTransport", first.TransportId)).GetProperty("error").GetString());
        using var driver = Client(host, first.Driver.AccountId, EmployeeRole.DRIVER);
        using var accepted = await driver.PostAsJsonAsync($"/api/v1/transports/{first.TransportId}/locations", first.Point(0.5m));
        accepted.EnsureSuccessStatusCode();
        var point = await owner.LocationAsync(); var staffPoint = await manager.LocationAsync();
        Assert.Equal(first.TransportId, point.GetProperty("transportId").GetGuid());
        Assert.Equal(first.TransportId, staffPoint.GetProperty("transportId").GetGuid());
        Assert.Equal(new[] { "transportId", "routePlanRouteId", "latitude", "longitude", "headingDegrees", "speedKmh", "accuracyMeters", "recordedAt" }.Order(),
            point.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(first.First.RoutePlanRouteId, point.GetProperty("routePlanRouteId").GetGuid());
        using var secondDriver = Client(host, second.Driver.AccountId, EmployeeRole.DRIVER);
        using var secondAccepted = await secondDriver.PostAsJsonAsync($"/api/v1/transports/{second.TransportId}/locations", second.Point(0.6m));
        secondAccepted.EnsureSuccessStatusCode();
        Assert.Equal(second.TransportId, (await stranger.LocationAsync()).GetProperty("transportId").GetGuid());
        Assert.False((await owner.InvokeAsync("LeaveTransport", first.TransportId)).TryGetProperty("error", out _));
        await using var reconnect = await Connection.OpenAsync(host, first.CustomerAccountId);
        await reconnect.InvokeAsync("JoinTransport", first.TransportId);
        using var viewer = Client(host, first.CustomerAccountId);
        Assert.Equal(0.5m, (await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/transports/{first.TransportId}/tracking"))
            .GetProperty("value").GetProperty("truck").GetProperty("longitude").GetDecimal());
        var account = await db.Accounts.SingleAsync(item => item.AccountId == first.CustomerAccountId);
        account.Status = AccountStatus.INACTIVE; await db.SaveChangesAsync();
        var next = first.Point(0.7m); next.RecordedAt = first.Clock.Now.AddSeconds(15);
        using var updated = await driver.PostAsJsonAsync($"/api/v1/transports/{first.TransportId}/locations", next);
        updated.EnsureSuccessStatusCode(); await manager.LocationAsync();
        await reconnect.InvokeAsync("LeaveTransport", first.TransportId);
        Assert.Equal(0, reconnect.LocationsSeen);
    }

    [Fact]
    public async Task Configuration_RejectsInvalidGeofenceAndAcceptsOverride()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await using var host = await StartAsync(db, f, 100);
        Assert.Equal(100, host.Services.GetRequiredService<IOptions<Tracking.TrackingOptions>>().Value.GeofenceRadiusMeters);
        await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(db, f, -1));
    }

    internal static async Task<WebApplication> StartAsync(AppDbContext db, Fixture f, double radius = 500)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15", ["JwtOptions:Issuer"] = "gps-tests", ["JwtOptions:Audience"] = "gps-tests",
            ["Tracking:GeofenceRadiusMeters"] = radius.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        builder.Services.AddSingleton(db); builder.Services.AddSingleton<TimeProvider>(f.Clock);
        builder.Services.AddApiControllers(); builder.Services.AddControllers().AddApplicationPart(typeof(TransportLocationsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration); builder.Services.AddRealtime(); builder.Services.AddSingleton(f.State);
        builder.Services.AddTransportPlanningModule();
        builder.Services.AddScoped<INotificationService, Pegaxus.Service.NotificationService.Service>();
        builder.Services.AddSingleton<INotificationRealtimeService, NoopNotificationRealtimeService>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        app.MapHub<TransportTrackingHub>("/hubs/transport-tracking", options => options.CloseOnAuthenticationExpiration = true);
        try { await app.StartAsync(); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    private static Uri Address(WebApplication host) => new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    private static string Token(WebApplication host, Guid accountId, EmployeeRole? role) => new JwtServices(host.Configuration).GenerateAccessToken(
        [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role.HasValue ? "Employee" : "Customer"),
            new Claim(ClaimTypes.Role, role.HasValue ? JwtServices.GetRoleName(role.Value) : JwtServices.GetRoleName(AccountType.CUSTOMER))]);
    private static HttpClient Client(WebApplication host, Guid accountId, EmployeeRole? role = null)
    {
        var client = new HttpClient { BaseAddress = Address(host) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(host, accountId, role)); return client;
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly ClientWebSocket socket = new();
        private readonly Queue<JsonElement> pending = new();
        private int invocation;
        internal int LocationsSeen { get; private set; }
        internal static async Task<Connection> OpenAsync(WebApplication host, Guid accountId, EmployeeRole? role = null)
        {
            var connection = new Connection();
            var uri = new UriBuilder(Address(host)) { Scheme = "ws", Path = "/hubs/transport-tracking", Query = "access_token=" + Uri.EscapeDataString(Token(host, accountId, role)) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await connection.socket.ConnectAsync(uri.Uri, timeout.Token);
            await connection.SendAsync(new { protocol = "json", version = 1 });
            Assert.Equal("{}", (await connection.FrameAsync()).GetRawText()); return connection;
        }
        internal async Task<JsonElement> InvokeAsync(string target, Guid transportId)
        {
            var id = (++invocation).ToString();
            await SendAsync(new { type = 1, invocationId = id, target, arguments = new[] { transportId } });
            while (true)
            {
                var frame = await FrameAsync();
                if (frame.TryGetProperty("type", out var type) && type.GetInt32() == 3 && frame.GetProperty("invocationId").GetString() == id) return frame;
            }
        }
        internal async Task<JsonElement> LocationAsync()
        {
            while (true)
            {
                var frame = await FrameAsync();
                if (frame.TryGetProperty("target", out var target) && target.GetString() == "LocationUpdated") return frame.GetProperty("arguments")[0];
            }
        }
        private async Task SendAsync(object value)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + '\u001e'), WebSocketMessageType.Text, true, timeout.Token);
        }
        private async Task<JsonElement> FrameAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (pending.Count == 0)
            {
                var bytes = new byte[16384]; using var message = new MemoryStream(); WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), timeout.Token);
                    Assert.NotEqual(WebSocketMessageType.Close, result.MessageType); message.Write(bytes, 0, result.Count);
                } while (!result.EndOfMessage);
                foreach (var part in Encoding.UTF8.GetString(message.ToArray()).Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                { using var json = JsonDocument.Parse(part); pending.Enqueue(json.RootElement.Clone()); }
            }
            var frame = pending.Dequeue();
            if (frame.TryGetProperty("target", out var target) && target.GetString() == "LocationUpdated") LocationsSeen++;
            return frame;
        }
        public ValueTask DisposeAsync() { socket.Abort(); socket.Dispose(); return ValueTask.CompletedTask; }
    }
}
