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

using Claims_ = Pegaxus.Service.ClaimService;

namespace Pegaxus.Service.Test;

public class ClaimHttpTests
{
    private const string Secret = "claims-tests-only-secret-key-with-more-than-32-characters";

    [Fact]
    public async Task Http_RolesEnvelopesAndTheCustomerVersusManagerShapeOfAClaim()
    {
        await using var db = TestSupport.CreateDb();
        var f = await ClaimTests.Fixture.CreateAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(ClaimsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "claims-test",
            ["JwtOptions:Audience"] = "claims-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(ClaimsController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Claims_.IClaimService>(_ => f.Service);
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

        // Without a token nothing works; staff roles other than the logistics manager are refused everywhere.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/claims")).StatusCode);
        var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        SignIn(client, driver.AccountId, JwtServices.GetRoleName(EmployeeRole.DRIVER));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/claims")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/bookings/{f.Booking.BookingId}/claim-eligibility")).StatusCode);

        // CL1
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        var eligibility = await client.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{f.Booking.BookingId}/claim-eligibility");
        Assert.Equal("Claim eligibility retrieved successfully", eligibility.GetProperty("message").GetString());
        var eligible = eligibility.GetProperty("value");
        Assert.Equal(new[] { "blockedReason", "booking", "canSubmit", "claimCount", "claimDeadline", "handover", "remainingSeconds", "serverTime", "transport" },
            eligible.EnumerateObject().Select(property => property.Name).Order());
        Assert.True(eligible.GetProperty("canSubmit").GetBoolean()); Assert.Equal(72_000, eligible.GetProperty("remainingSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null, eligible.GetProperty("blockedReason").ValueKind);

        // CL2: a manager cannot submit, the owner can; the customer view hides every manager-only block.
        SignIn(client, f.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/bookings/{f.Booking.BookingId}/claims", ClaimTests.Fixture.Request())).StatusCode);
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        var invalid = await client.PostAsJsonAsync($"/api/v1/bookings/{f.Booking.BookingId}/claims", new { claimType = "HORSE_INJURY", title = "short", description = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", invalidBody.GetProperty("errorCode").GetString());
        Assert.Contains(invalidBody.GetProperty("errors").EnumerateArray(), item => item.GetProperty("field").GetString() == "title");
        var created = await client.PostAsJsonAsync($"/api/v1/bookings/{f.Booking.BookingId}/claims", ClaimTests.Fixture.Request());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var envelope = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(201, envelope.GetProperty("code").GetInt32()); Assert.Equal("Claim submitted successfully", envelope.GetProperty("message").GetString());
        var customerClaim = envelope.GetProperty("value");
        Assert.Equal(new[] { "availableActions", "booking", "cancellationReason", "cancelledAt", "claimCode", "claimId", "claimType", "createdAt", "currency", "customer", "customerNote",
            "description", "evidenceNote", "handover", "requestedCompensationAmount", "resolution", "resolvedAt", "reviewedAt", "status", "submittedAt", "title", "transport", "updatedAt" },
            customerClaim.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(new[] { "claimDeadline", "completedAt", "handoverCode", "handoverRecordId", "handoverType", "status" },
            customerClaim.GetProperty("handover").EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("PENDING", customerClaim.GetProperty("status").GetString()); Assert.Equal("CANCEL", Assert.Single(customerClaim.GetProperty("availableActions").EnumerateArray()).GetString());
        var claimId = customerClaim.GetProperty("claimId").GetGuid();

        // CL4: the manager sees the extra blocks, and the first open moves updatedAt.
        SignIn(client, f.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var managerClaim = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/claims/{claimId}")).GetProperty("value");
        foreach (var field in new[] { "pickupHandover", "healthLogSummary", "incidents", "billing" }) Assert.True(managerClaim.TryGetProperty(field, out _), field);
        Assert.Equal(new[] { "billing", "healthLogSummary", "incidents", "pickupHandover" }.Order(),
            managerClaim.EnumerateObject().Select(property => property.Name).Except(customerClaim.EnumerateObject().Select(property => property.Name)).Order());
        Assert.True(managerClaim.GetProperty("handover").TryGetProperty("senderSignatureUrl", out _));
        Assert.NotEqual(JsonValueKind.Null, managerClaim.GetProperty("reviewedAt").ValueKind);
        Assert.Equal("NO_FINAL_INVOICE", managerClaim.GetProperty("billing").GetProperty("warning").GetString());

        // CL5 as customer is refused; as manager it answers 201 and the customer view of the outcome has no internal fields.
        var resolveBody = new { lastUpdatedAt = managerClaim.GetProperty("updatedAt").GetDateTimeOffset(), resolutionType = "PARTIAL_COMPENSATION", approvedCompensationAmount = 18_000_000,
            resolutionSummary = "The company covers part of the veterinary treatment.", correctiveAction = "Review the box padding.", internalNote = "Agreed by phone." };
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/resolution", resolveBody)).StatusCode);
        SignIn(client, f.ManagerAccount.AccountId, JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER));
        var resolved = await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/resolution", resolveBody);
        Assert.Equal(HttpStatusCode.Created, resolved.StatusCode);
        var resolvedBody = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Claim approved successfully", resolvedBody.GetProperty("message").GetString());
        Assert.Equal("internalNote", resolvedBody.GetProperty("value").GetProperty("resolution").EnumerateObject().Select(property => property.Name).Single(name => name == "internalNote"));
        var again = await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/resolution", resolveBody);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("CLAIM_ALREADY_RESOLVED", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        var customerResolution = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/claims/{claimId}")).GetProperty("value").GetProperty("resolution");
        Assert.Equal(new[] { "approvedCompensationAmount", "claimResolutionId", "currency", "resolutionSummary", "resolutionType", "resolvedAt" },
            customerResolution.EnumerateObject().Select(property => property.Name).Order());

        // CL3 and the error mapping of the rest.
        var list = (await client.GetFromJsonAsync<JsonElement>("/api/v1/claims?status=APPROVED")).GetProperty("value");
        Assert.Equal(1, list.GetProperty("totalItems").GetInt32()); Assert.Equal(JsonValueKind.Number, list.GetProperty("items")[0].GetProperty("approvedCompensationAmount").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/claims?pageIndex=0")).StatusCode);
        SignIn(client, f.StrangerAccount.AccountId, "Customer");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/claims/{claimId}")).StatusCode);
        SignIn(client, f.CustomerAccount.AccountId, "Customer");
        var cancel = await client.PostAsJsonAsync($"/api/v1/claims/{claimId}/cancel", new { lastUpdatedAt = DateTimeOffset.UtcNow, cancellationReason = "A reason that is long enough" });
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("INVALID_CLAIM_STATUS", (await cancel.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        await app.StopAsync();
    }

    [Fact]
    public async Task Http_ASubmissionAfterTheWindowIs422()
    {
        await using var db = TestSupport.CreateDb();
        var f = await ClaimTests.Fixture.CreateAsync(db, deliveredHoursAgo: 30);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ApplicationName = typeof(ClaimsController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = Secret, ["JwtOptions:Issuer"] = "claims-test",
            ["JwtOptions:Audience"] = "claims-test", ["JwtOptions:ExpirationMinutes"] = "15"
        });
        builder.Services.AddSingleton(db); builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(ClaimsController).Assembly);
        builder.Services.AddScoped<GlobalExceptionHandlerMiddleware>();
        builder.Services.AddScoped<Claims_.IClaimService>(_ => f.Service);
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        SignIn(client, f.CustomerAccount.AccountId, "Customer");

        var response = await client.PostAsJsonAsync($"/api/v1/bookings/{f.Booking.BookingId}/claims", ClaimTests.Fixture.Request());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("CLAIM_WINDOW_EXPIRED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        await app.StopAsync();
    }

    private static void SignIn(HttpClient client, Guid accountId, string role)
    {
        var token = new JwtSecurityToken("claims-test", "claims-test",
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
}
