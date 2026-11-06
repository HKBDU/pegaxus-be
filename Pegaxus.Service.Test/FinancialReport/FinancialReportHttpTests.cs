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

using Reports = Pegaxus.Service.FinancialReportService;

namespace Pegaxus.Service.Test;

public class FinancialReportHttpTests
{
    private const string Secret = "financial-tests-only-secret-key-with-more-than-32-characters";

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(AppDbContext db, FinancialReportTests.Fixture f)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(FinancialReportsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "financial-test",
            ["JwtOptions:Audience"] = "financial-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(FinancialReportsController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Reports.IFinancialReportService>(_ => f.Service);
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) });
    }

    [Fact]
    public async Task Http_RolesEnvelopesAndTheShapeOfTheThreeCalls()
    {
        await using var db = TestSupport.CreateDb();
        var f = await FinancialReportTests.Fixture.CreateAsync(db, TransportStatus.IN_TRANSIT);
        var (app, client) = await StartAsync(db, f);
        using var _ = client; await using var __ = app;
        var url = $"/api/v1/transports/{f.Transport.TransportId}/financial-report";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        SignIn(client, coordinator.AccountId, JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(url + "/refresh", null)).StatusCode);

        // FS1 on an unfinished trip: refused, then provisional by query string.
        SignIn(client, f.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var refused = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("TRANSPORT_NOT_COMPLETED", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        var provisional = await client.GetFromJsonAsync<JsonElement>(url + "?allowProvisional=true");
        Assert.Equal("Financial report retrieved successfully", provisional.GetProperty("message").GetString());
        var value = provisional.GetProperty("value");
        Assert.True(value.GetProperty("isProvisional").GetBoolean()); Assert.Equal(JsonValueKind.Null, value.GetProperty("reportCode").ValueKind);
        Assert.Equal("TRANSPORT_NOT_COMPLETED", Assert.Single(value.GetProperty("warnings").EnumerateArray()).GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(url + "/refresh", null)).StatusCode);

        // Once the trip is done: the saved report, with every documented field.
        f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var done = (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("value");
        Assert.Equal(new[] { "booking", "currency", "createdAt", "dossierTemplateId", "financialReportId", "generatedAt", "isProvisional", "isStale", "outstandingAmount", "reconciliation",
            "reportCode", "serverTime", "suggestedItems", "summaryNote", "totalEmergencyCost", "totalInvoicedAmount", "totalPaidAmount", "transport", "updatedAt", "warnings" }.Order(),
            done.EnumerateObject().Select(property => property.Name).Order());
        Assert.False(done.GetProperty("isProvisional").GetBoolean()); Assert.Equal(JsonValueKind.Number, done.GetProperty("totalInvoicedAmount").ValueKind);
        Assert.Equal(new[] { "basis", "description", "itemType", "quantity", "referenceAmount", "sourceRefs", "suggestedPrice", "warning" },
            done.GetProperty("suggestedItems")[0].EnumerateObject().Select(property => property.Name).Order());
        Assert.False(done.GetProperty("reconciliation").GetProperty("hasMismatch").GetBoolean());

        // FS2
        var refreshed = await client.PostAsync(url + "/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal("Financial report refreshed successfully", (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());

        // FS3
        var version = (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("value").GetProperty("updatedAt").GetDateTimeOffset();
        var tooLong = await client.PatchAsJsonAsync(url, new { lastUpdatedAt = version, summaryNote = new string('x', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains((await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray(), item => item.GetProperty("field").GetString() == "summaryNote");
        var saved = await client.PatchAsJsonAsync(url, new { lastUpdatedAt = version, summaryNote = "Margin is good" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedBody = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Financial report note saved successfully", savedBody.GetProperty("message").GetString()); Assert.Equal("Margin is good", savedBody.GetProperty("value").GetProperty("summaryNote").GetString());
        var stale = await client.PatchAsJsonAsync(url, new { lastUpdatedAt = version, summaryNote = "again" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("FINANCIAL_REPORT_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/transports/{Guid.NewGuid()}/financial-report")).StatusCode);
    }

    private static void SignIn(HttpClient client, Guid accountId, string role)
    {
        var token = new JwtSecurityToken("financial-test", "financial-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
