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

using Reports = Pegaxus.Service.ReportService;

namespace Pegaxus.Service.Test;

public class ReportHttpTests
{
    private const string Secret = "report-tests-only-secret-key-with-more-than-32-characters";
    private const string Range = "dateFrom=2026-10-01&dateTo=2026-10-31";

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(AppDbContext db, ReportTests.Fixture f)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(ReportsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "report-test",
            ["JwtOptions:Audience"] = "report-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(ReportsController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Reports.IReportService>(_ => f.Service);
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
    }

    private static void SignIn(HttpClient client, Guid accountId, string role)
    {
        var token = new JwtSecurityToken("report-test", "report-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(property => property.Name).Order().ToArray();

    [Fact]
    public async Task Http_OnlyLogisticsManagersGetInAndTheEnvelopesAreRight()
    {
        await using var db = TestSupport.CreateDb();
        var s = await ReportTests.SeedOnTimeAsync(db);
        var (app, client) = await StartAsync(db, s.F);
        using var _ = client; await using var __ = app;
        var urls = new[] { "on-time-delivery", "incident-claim-frequency", "transit-time", "transit-time/export" }.Select(path => $"/api/v1/reports/{path}?{Range}&format=pdf").ToArray();

        foreach (var url in urls) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        SignIn(client, s.F.CustomerAccount.AccountId, "Customer");
        foreach (var url in urls) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        SignIn(client, coordinator.AccountId, JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR));
        foreach (var url in urls) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);

        SignIn(client, s.F.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var onTime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/reports/on-time-delivery?{Range}&trendBy=WEEK&pageSize=1");
        Assert.Equal("On-time delivery report retrieved successfully", onTime.GetProperty("message").GetString());
        var value = onTime.GetProperty("value");
        Assert.Equal(new[] { "byProvider", "delayCauses", "distribution", "excluded", "generatedAt", "items", "kpi", "pageIndex", "pageSize", "period", "totalItems", "totalPages", "trend" }, Names(value));
        Assert.Equal(new[] { "avgDelayMinutes", "meetsSla", "onTimeRate", "slaTargetRate", "totalTransports" }, Names(value.GetProperty("kpi")));
        Assert.Equal(new[] { "change", "previous", "value" }, Names(value.GetProperty("kpi").GetProperty("onTimeRate")));
        Assert.Equal(66.7m, value.GetProperty("kpi").GetProperty("onTimeRate").GetProperty("value").GetDecimal());
        Assert.Equal("2026-10-01", value.GetProperty("period").GetProperty("dateFrom").GetString());
        Assert.Equal("2026-08-31", value.GetProperty("period").GetProperty("previousDateFrom").GetString());
        Assert.Equal(1, value.GetProperty("items").GetArrayLength()); Assert.Equal(2, value.GetProperty("totalPages").GetInt32());
        Assert.Equal(new[] { "actualArrivalAt", "bookingCode", "bucket", "delayMinutes", "destinationCountry", "mainCause", "originCountry", "plannedEndAt", "transportCode", "transportId" },
            Names(value.GetProperty("items")[0]));
        Assert.Equal(new[] { "MAJOR_DELAY", "MINOR_DELAY", "ON_TIME" }, Names(value.GetProperty("distribution")));

        var incident = await client.GetFromJsonAsync<JsonElement>($"/api/v1/reports/incident-claim-frequency?{Range}");
        Assert.Equal("Incident and claim frequency report retrieved successfully", incident.GetProperty("message").GetString());
        Assert.Equal(new[] { "claimsByStatus", "claimsByType", "emergencyCostByCategory", "generatedAt", "incidentsByCategory", "incidentsBySeverity", "incidentsByType", "items", "kpi", "pageIndex",
            "pageSize", "period", "totalItems", "totalPages", "vehicleReliability" }, Names(incident.GetProperty("value")));
        Assert.Equal("VND", incident.GetProperty("value").GetProperty("kpi").GetProperty("currency").GetString());

        var transit = await client.GetFromJsonAsync<JsonElement>($"/api/v1/reports/transit-time?{Range}&delayFilter=EARLY");
        Assert.Equal("Transit time report retrieved successfully", transit.GetProperty("message").GetString());
        Assert.Equal(new[] { "distribution", "excluded", "generatedAt", "items", "kpi", "pageIndex", "pageSize", "period", "routeBenchmark", "totalItems", "totalPages" }, Names(transit.GetProperty("value")));
    }

    [Fact]
    public async Task Http_ABadRangeOrFilterIsA400WithFieldErrors()
    {
        await using var db = TestSupport.CreateDb();
        var f = await ReportTests.Fixture.CreateAsync(db);
        var (app, client) = await StartAsync(db, f);
        using var _ = client; await using var __ = app;
        SignIn(client, f.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));

        var missing = await client.GetAsync("/api/v1/reports/on-time-delivery");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var body = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("errorCode").GetString());
        Assert.Equal(new[] { "dateFrom", "dateTo" }, body.GetProperty("errors").EnumerateArray().Select(item => item.GetProperty("field").GetString()!).Order());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/reports/transit-time?dateFrom=2026-13-45&dateTo=2026-10-31")).StatusCode);
        var reversed = await client.GetAsync("/api/v1/reports/incident-claim-frequency?dateFrom=2026-10-31&dateTo=2026-10-01");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        var wrongBucket = await client.GetAsync($"/api/v1/reports/on-time-delivery?{Range}&bucket=LATE");
        Assert.Contains((await wrongBucket.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray(), item => item.GetProperty("field").GetString() == "bucket");
    }

    [Fact]
    public async Task Http_ExportReturnsTheFileNotTheEnvelope_AndTheErrorsKeepTheirCodes()
    {
        await using var db = TestSupport.CreateDb();
        var s = await ReportTests.SeedOnTimeAsync(db);
        var (app, client) = await StartAsync(db, s.F);
        using var _ = client; await using var __ = app;
        SignIn(client, s.F.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));

        var xlsx = await client.GetAsync($"/api/v1/reports/on-time-delivery/export?{Range}&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal("on-time-delivery-2026-10-01_2026-10-31.xlsx", xlsx.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("attachment", xlsx.Content.Headers.ContentDisposition.DispositionType);
        Assert.Equal("PK", Encoding.ASCII.GetString((await xlsx.Content.ReadAsByteArrayAsync())[..2]));

        var pdf = await client.GetAsync($"/api/v1/reports/transit-time/export?{Range}&format=pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal("transit-time-2026-10-01_2026-10-31.pdf", pdf.Content.Headers.ContentDisposition!.FileName);

        var unknown = await client.GetAsync($"/api/v1/reports/weekly/export?{Range}&format=pdf");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/reports/transit-time/export?{Range}&format=csv")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/reports/transit-time/export?{Range}")).StatusCode);

        var empty = await client.GetAsync("/api/v1/reports/incident-claim-frequency/export?dateFrom=2025-01-01&dateTo=2025-01-31&format=xlsx");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        var error = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NO_DATA_TO_EXPORT", error.GetProperty("errorCode").GetString());
        Assert.Equal("There is no data to export for the selected filters", error.GetProperty("message").GetString());
    }
}
