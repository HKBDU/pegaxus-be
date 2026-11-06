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
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;

using Audit = Pegaxus.Service.AuditService;
using static Pegaxus.Service.Test.AuditSupport;

namespace Pegaxus.Service.Test;

public class AuditHttpTests
{
    private const string Secret = "audit-tests-only-secret-key-with-more-than-32-characters";

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(AppDbContext db, Fixture f)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(AuditController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "audit-test",
            ["JwtOptions:Audience"] = "audit-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(AuditController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Audit.IAuditService>(_ => f.Service);
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
    }

    private static void SignIn(HttpClient client, Guid accountId, string role)
    {
        var token = new JwtSecurityToken("audit-test", "audit-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(property => property.Name).Order().ToArray();

    [Fact]
    public async Task Http_RolesAndShapesOfTheArchiveAndTheAudit()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var (app, client) = await StartAsync(db, f);
        using var _ = client; await using var __ = app;
        var id = f.Transport.TransportId;
        var archive = "/api/v1/transport-archive?search=985120"; var audit = $"/api/v1/transports/{id}/audit"; var export = $"/api/v1/audit-exports/{Guid.NewGuid()}";

        foreach (var url in new[] { archive, audit, export }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/v1/transports/{id}/audit-exports", new { format = "ZIP", mode = "STANDARD" })).StatusCode);

        SignIn(client, f.CustomerId, "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(archive)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(export)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/transports/{id}/audit-exports", new { format = "ZIP", mode = "STANDARD" })).StatusCode);
        var own = await client.GetFromJsonAsync<JsonElement>($"{audit}?blocks=booking,finance");
        Assert.Equal("CUSTOMER", own.GetProperty("value").GetProperty("viewMode").GetString());
        Assert.Equal(new[] { "booking", "generatedAt", "timeline", "transport", "viewMode" }, Names(own.GetProperty("value")));
        SignIn(client, f.OtherCustomerAccount.AccountId, "Customer");
        var foreign = await client.GetAsync(audit);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());

        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        SignIn(client, coordinator.AccountId, JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(audit)).StatusCode);

        SignIn(client, f.ManagerId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var list = await client.GetFromJsonAsync<JsonElement>(archive);
        Assert.Equal("Transport archive retrieved successfully", list.GetProperty("message").GetString());
        Assert.Equal(new[] { "items", "pageIndex", "pageSize", "totalItems", "totalPages" }, Names(list.GetProperty("value")));
        var item = list.GetProperty("value").GetProperty("items")[0];
        Assert.Equal(new[] { "bookingCode", "bookingId", "claimCount", "completedAt", "customerName", "destinationCountry", "horses", "incidentCount", "originCountry", "status", "transportCode", "transportId" }, Names(item));
        Assert.Equal(new[] { "horseId", "microchipNumber", "name", "passportNumber" }, Names(item.GetProperty("horses")[0]));

        var full = await client.GetFromJsonAsync<JsonElement>(audit);
        Assert.Equal("Transport audit retrieved successfully", full.GetProperty("message").GetString());
        Assert.Equal(new[] { "booking", "compliance", "finance", "generatedAt", "horses", "operations", "routing", "timeline", "transport", "viewMode" }, Names(full.GetProperty("value")));
        var some = await client.GetFromJsonAsync<JsonElement>($"{audit}?blocks=operations&limit=2");
        Assert.Equal(new[] { "generatedAt", "operations", "timeline", "transport", "viewMode" }, Names(some.GetProperty("value")));
        Assert.Equal(2, some.GetProperty("value").GetProperty("timeline").GetArrayLength());
        Assert.Equal(new[] { "at", "refId", "source", "title", "visibility" }, Names(some.GetProperty("value").GetProperty("timeline")[0]));

        var bad = await client.GetAsync($"{audit}?blocks=nope&limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(new[] { "blocks", "limit" }, (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()!).Order());
    }

    [Fact]
    public async Task Http_AnExportIsAcceptedThenPolledAndTheErrorsKeepTheirCodes()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var (app, client) = await StartAsync(db, f);
        using var _ = client; await using var __ = app;
        SignIn(client, f.ManagerId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var url = $"/api/v1/transports/{f.Transport.TransportId}/audit-exports";

        var accepted = await client.PostAsJsonAsync(url, new { format = "ZIP", mode = "INSURANCE" });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((202, "Audit export queued"), (body.GetProperty("code").GetInt32(), body.GetProperty("message").GetString()));
        var job = body.GetProperty("value");
        Assert.Equal(new[] { "exportId", "format", "mode", "requestedAt", "status", "transportId" }, Names(job));
        Assert.Equal(("ZIP", "INSURANCE", "QUEUED"), (job.GetProperty("format").GetString(), job.GetProperty("mode").GetString(), job.GetProperty("status").GetString()));

        await f.Service.ProcessExportAsync(job.GetProperty("exportId").GetGuid());
        var ready = await client.GetFromJsonAsync<JsonElement>($"/api/v1/audit-exports/{job.GetProperty("exportId").GetGuid()}");
        Assert.Equal("Audit export retrieved successfully", ready.GetProperty("message").GetString());
        var value = ready.GetProperty("value");
        Assert.Equal(new[] { "completedAt", "delivery", "downloadUrl", "downloadUrlExpiresAt", "errorMessage", "exportId", "fileName", "format", "mode", "requestedAt", "sha256", "sizeBytes", "status", "transportCode", "transportId" }, Names(value));
        Assert.Equal(("READY", "DIRECT"), (value.GetProperty("status").GetString(), value.GetProperty("delivery").GetString()));
        Assert.StartsWith("https://signed.example.com/", value.GetProperty("downloadUrl").GetString());

        var invalid = await client.PostAsJsonAsync(url, new { format = "RAR" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(new[] { "format", "mode" }, (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray().Select(error => error.GetProperty("field").GetString()!).Order());
        var onTheRoad = f.R.AddTransport("2026-10-25 10:00", status: TransportStatus.IN_TRANSIT); await db.SaveChangesAsync();
        var notDone = await client.PostAsJsonAsync($"/api/v1/transports/{onTheRoad.TransportId}/audit-exports", new { format = "PDF", mode = "STANDARD" });
        Assert.Equal(HttpStatusCode.Conflict, notDone.StatusCode);
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await notDone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/transports/{Guid.NewGuid()}/audit-exports", new { format = "PDF", mode = "STANDARD" })).StatusCode);
        var gone = await client.GetAsync($"/api/v1/audit-exports/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("EXPORT_NOT_FOUND", (await gone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }
}
