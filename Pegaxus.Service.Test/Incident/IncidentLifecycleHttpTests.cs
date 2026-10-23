using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.JwtService;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentHttpTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Claim = System.Security.Claims.Claim;

namespace Pegaxus.Service.Test;

public class IncidentLifecycleHttpTests
{
    [Fact]
    public async Task CustomerDetail_ContainsAllowedDetails_RecursivelyOmitsFinanceActionsAndPhones_LeavesStatusUntouched()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var report = f.Report(); report.Category = "BOTH"; report.EstimatedRepairCost = 321_987; report.PhotoFiles = [Photo()];
        var item = (await f.Service.ReportVehicleAsync(report, f.Driver.AccountId)).Value;
        var horseId = await db.BookingHorses.Where(horse => horse.BookingId == f.Transport.BookingId).Select(horse => horse.HorseId).FirstAsync();
        await f.Service.AddHorseDetailsAsync(item.IncidentReportId, new() { Horses = [new() { HorseId = horseId, IncidentType = "STRESS",
            ConditionDescription = "The horse is sweating after the stop" }] }, f.Escort.AccountId);
        AddCost(db, await db.IncidentReports.SingleAsync(), f.Driver, EmergencyCostStatus.APPROVED); await db.SaveChangesAsync();
        var notificationCount = await db.Notifications.CountAsync(); var version = (await db.IncidentReports.SingleAsync()).UpdatedAt;
        await using var host = await StartAsync(db, f.Storage); using var customer = CustomerClient(host, f.CustomerAccountId);
        using var response = await customer.GetAsync($"/api/v1/incidents/{item.IncidentReportId}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(); var value = root.GetProperty("value");
        Assert.Equal("Incident retrieved successfully", root.GetProperty("message").GetString());
        Assert.Equal(report.Description, value.GetProperty("description").GetString()); Assert.Equal("CRITICAL", value.GetProperty("severity").GetString());
        Assert.Single(value.GetProperty("horseDetails").EnumerateArray()); Assert.Single(value.GetProperty("photos").EnumerateArray());
        Assert.Equal(f.Driver.FullName, value.GetProperty("reportedBy").GetProperty("fullName").GetString());
        string[] hidden = ["phone", "emergencyContacts", "safety", "availableActions", "canResolve", "effects", "emergencySettlementRequired",
            "costRequests", "costRequestCount", "costSummary", "pendingCostCount", "approvedTotal", "requestedTotal", "requestedAmount", "approvedAmount", "estimatedRepairCost"];
        AssertNoProperties(value, hidden); Assert.DoesNotContain("321987", value.GetRawText());
        using var manager = Client(host, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var staff = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/incidents/{item.IncidentReportId}");
        Assert.True(staff.GetProperty("value").TryGetProperty("costRequests", out _));
        Assert.Equal(version, (await db.IncidentReports.SingleAsync()).UpdatedAt);
        Assert.Equal(IncidentStatus.REPORTED, (await db.IncidentReports.SingleAsync()).Status); Assert.Equal(notificationCount, await db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData(IncidentSeverity.LOW, true, HttpStatusCode.NotFound)]
    [InlineData(IncidentSeverity.MEDIUM, true, HttpStatusCode.NotFound)]
    [InlineData(IncidentSeverity.HIGH, true, HttpStatusCode.OK)]
    [InlineData(IncidentSeverity.CRITICAL, true, HttpStatusCode.OK)]
    [InlineData(IncidentSeverity.HIGH, false, HttpStatusCode.NotFound)]
    public async Task CustomerDetail_RequiresOwnBookingAndSeriousIncident(IncidentSeverity severity, bool owner, HttpStatusCode expected)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f, severity: severity); await db.SaveChangesAsync();
        var accountId = owner ? f.CustomerAccountId : (await TestSupport.AddCustomerAsync(db)).Account.AccountId;
        await using var host = await StartAsync(db, f.Storage); using var client = CustomerClient(host, accountId);
        using var response = await client.GetAsync($"/api/v1/incidents/{item.IncidentReportId}"); Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.NotFound)
            Assert.Equal("INCIDENT_NOT_FOUND", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Http_RejectsAnonymousAndCustomerListOrWrites()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage); using var client = CustomerClient(host, f.CustomerAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/incidents")).StatusCode);
        foreach (var action in new[] { "resolve", "cancel", "self-resolve", "raise-severity" })
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/incidents/{item.IncidentReportId}/{action}", new { });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/incidents/{item.IncidentReportId}")).StatusCode);
        Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("resolve", "RESOLVED")]
    [InlineData("cancel", "CANCELLED")]
    [InlineData("self-resolve", "RESOLVED")]
    [InlineData("raise-severity", "REPORTED")]
    public async Task Lifecycle_ReturnsContractEnvelope_RejectsStaleVersion(string action, string status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage); var actor = action == "self-resolve" ? f.Escort : f.Manager;
        using var client = Client(host, actor.AccountId, actor.Role); var path = $"/api/v1/incidents/{item.IncidentReportId}/{action}";
        var body = new { lastUpdatedAt = DbTime.AsUtc(item.UpdatedAt.AddSeconds(-1)), resolutionSummary = Summary,
            emergencySettlementConfirmed = true, cancellationReason = Reason, severity = "CRITICAL", reason = Reason };
        using var stale = await client.PostAsJsonAsync(path, body); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("INCIDENT_MODIFIED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        using var response = await client.PostAsJsonAsync(path, body with { lastUpdatedAt = DbTime.AsUtc(item.UpdatedAt) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(root.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, root.GetProperty("code").GetInt32());
        var value = root.GetProperty("value"); Assert.Equal(status, value.GetProperty("status").GetString());
        Assert.Equal(action == "raise-severity" ? "CRITICAL" : "HIGH", value.GetProperty("severity").GetString());
        if (action == "raise-severity")
        {
            Assert.Equal($"Incident {item.IncidentCode} severity raised from HIGH to CRITICAL", root.GetProperty("message").GetString());
            var effects = value.GetProperty("effects"); Assert.True(effects.GetProperty("customerNotified").GetBoolean());
            Assert.True(effects.GetProperty("emailQueued").GetBoolean()); Assert.Equal("URGENT", effects.GetProperty("notifiedPriority").GetString());
        }
        else { Assert.Empty(value.GetProperty("availableActions").EnumerateArray()); Assert.False(value.GetProperty("canResolve").GetBoolean()); }
    }

    [Theory]
    [InlineData("resolve", EmergencyCostStatus.PENDING, "UNRESOLVED_EMERGENCY_COSTS", "pendingRequests", "requestedAmount")]
    [InlineData("cancel", EmergencyCostStatus.APPROVED, "HAS_APPROVED_EMERGENCY_COST", "approvedRequests", "approvedAmount")]
    [InlineData("self-resolve", EmergencyCostStatus.APPROVED, "HAS_OPEN_EMERGENCY_COSTS", "requests", "status")]
    public async Task Lifecycle_CostErrorsReturnStructured422Value(string action, EmergencyCostStatus status, string code, string arrayName, string field)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var cost = AddCost(db, item, f.Driver, status); await db.SaveChangesAsync(); await using var host = await StartAsync(db, f.Storage);
        var actor = action == "self-resolve" ? f.Driver : f.Manager; using var client = Client(host, actor.AccountId, actor.Role);
        using var response = await client.PostAsJsonAsync($"/api/v1/incidents/{item.IncidentReportId}/{action}", new
            { lastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), resolutionSummary = Summary, emergencySettlementConfirmed = true, cancellationReason = Reason });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode); var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, root.GetProperty("errorCode").GetString());
        var blocked = Assert.Single(root.GetProperty("value").GetProperty(arrayName).EnumerateArray());
        Assert.Equal(cost.RequestCode, blocked.GetProperty("requestCode").GetString()); Assert.True(blocked.TryGetProperty(field, out _));
        Assert.Equal(IncidentStatus.REPORTED, item.Status); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("resolve", "version", "VALIDATION_ERROR")]
    [InlineData("resolve", "summary", "VALIDATION_ERROR")]
    [InlineData("resolve", "settlement", "SETTLEMENT_NOT_CONFIRMED")]
    [InlineData("cancel", "reason", "VALIDATION_ERROR")]
    [InlineData("self-resolve", "summary", "VALIDATION_ERROR")]
    [InlineData("raise-severity", "reason", "VALIDATION_ERROR")]
    [InlineData("raise-severity", "severity", "VALIDATION_ERROR")]
    public async Task Lifecycle_ValidatesRequestBeforeMutation(string action, string invalidField, string code)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage); var actor = action == "self-resolve" ? f.Driver : f.Manager;
        using var client = Client(host, actor.AccountId, actor.Role);
        using var response = await client.PostAsJsonAsync($"/api/v1/incidents/{item.IncidentReportId}/{action}", new
        {
            lastUpdatedAt = invalidField == "version" ? (DateTimeOffset?)null : DbTime.AsUtc(item.UpdatedAt),
            resolutionSummary = invalidField == "summary" ? "   Too short   " : Summary,
            emergencySettlementConfirmed = invalidField != "settlement", cancellationReason = invalidField == "reason" ? "  tiny  " : Reason,
            severity = invalidField == "severity" ? "5" : "CRITICAL", reason = invalidField == "reason" ? "   " : Reason
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task List_BindsFilters_ReturnsEnvelope_RejectsInvalidPagingAndMalformedDates()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var high = AddIncident(db, f); AddIncident(db, f, IncidentStatus.RESOLVED); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage); using var client = Client(host, f.Specialist.AccountId, EmployeeRole.TRANSPORT_SPECIALIST);
        var root = await client.GetFromJsonAsync<JsonElement>("/api/v1/incidents?status=REPORTED&severity=HIGH&pageIndex=1&pageSize=1&sortBy=updatedAt:desc");
        Assert.Equal("Incidents retrieved successfully", root.GetProperty("message").GetString()); var value = root.GetProperty("value");
        Assert.Equal(1, value.GetProperty("totalItems").GetInt32()); Assert.Equal(1, value.GetProperty("counts").GetProperty("resolved").GetInt32());
        Assert.Equal(high.IncidentReportId, Assert.Single(value.GetProperty("items").EnumerateArray()).GetProperty("incidentReportId").GetGuid());
        foreach (var query in new[] { "pageSize=101", "pageIndex=0", "occurredFrom=not-a-date", "sortBy=title:asc", "transportId=bad-id", "hasPendingCost=invalid" })
        { using var response = await client.GetAsync("/api/v1/incidents?" + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
    }

    internal static HttpClient CustomerClient(WebApplication host, Guid accountId)
    {
        var client = Client(host, accountId, EmployeeRole.DRIVER);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, "Customer")]));
        return client;
    }

    internal static void AssertNoProperties(JsonElement value, IReadOnlyCollection<string> forbidden)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            { Assert.DoesNotContain(property.Name, forbidden); AssertNoProperties(property.Value, forbidden); }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) AssertNoProperties(item, forbidden);
    }
}
