using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;

using static Pegaxus.Service.Test.EmergencyCostTests;
using static Pegaxus.Service.Test.IncidentLifecycleHttpTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentHttpTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

namespace Pegaxus.Service.Test;

public class EmergencyCostHttpTests
{
    [Fact]
    public async Task CostEndpoints_ReturnContractEnvelopesContextAndConflictMetadata()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage);
        using var driver = Client(host, f.Driver.AccountId, f.Driver.Role); using var manager = Client(host, f.Manager.AccountId, f.Manager.Role);
        using var created = await driver.PostAsJsonAsync($"/api/v1/incidents/{incident.IncidentReportId}/emergency-cost-requests", CostRequest());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var envelope = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(envelope.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(201, envelope.GetProperty("code").GetInt32()); var cost = envelope.GetProperty("value");
        Assert.Equal("PENDING", cost.GetProperty("status").GetString()); Assert.Equal(15000000, cost.GetProperty("requestedAmount").GetInt64());
        Assert.Equal(f.Driver.EmployeeId, cost.GetProperty("requestedBy").GetProperty("employeeId").GetGuid());
        Assert.Equal(JsonValueKind.Null, cost.GetProperty("approvedAmount").ValueKind);
        var id = cost.GetProperty("emergencyCostRequestId").GetGuid(); var url = $"/api/v1/emergency-cost-requests/{id}";
        var list = await manager.GetFromJsonAsync<JsonElement>("/api/v1/emergency-cost-requests");
        Assert.Equal("Emergency cost requests retrieved successfully", list.GetProperty("message").GetString());
        Assert.Equal(1, list.GetProperty("value").GetProperty("counts").GetProperty("pending").GetInt32());
        Assert.Equal(1, list.GetProperty("value").GetProperty("totalItems").GetInt32());
        var detail = (await manager.GetFromJsonAsync<JsonElement>(url)).GetProperty("value");
        Assert.Equal("DECIDE", detail.GetProperty("availableActions")[0].GetString());
        Assert.Equal(incident.IncidentReportId, detail.GetProperty("incidentSummary").GetProperty("incidentReportId").GetGuid());
        Assert.Equal(f.Segment.RoutePlanId, detail.GetProperty("currentRoute").GetProperty("routePlanId").GetGuid());
        var request = new { lastUpdatedAt = cost.GetProperty("updatedAt").GetDateTimeOffset(), decision = "APPROVE", approvedAmount = 10000000, decisionReason = Reason };
        using var approved = await manager.PostAsJsonAsync(url + "/decision", request); Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approval = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10000000, approval.GetProperty("value").GetProperty("approvedAmount").GetInt64());
        Assert.Equal("APPROVED", approval.GetProperty("value").GetProperty("decision").GetProperty("decision").GetString());
        using var duplicate = await manager.PostAsJsonAsync(url + "/decision", request); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var conflict = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("EMERGENCY_COST_ALREADY_DECIDED", conflict.GetProperty("errorCode").GetString());
        Assert.Equal(f.Manager.EmployeeId, conflict.GetProperty("value").GetProperty("decidedBy").GetProperty("employeeId").GetGuid());
        Assert.Equal("APPROVED", conflict.GetProperty("value").GetProperty("status").GetString());
        list = await manager.GetFromJsonAsync<JsonElement>("/api/v1/emergency-cost-requests");
        Assert.Equal(0, list.GetProperty("value").GetProperty("totalItems").GetInt32());
        Assert.Equal(1, list.GetProperty("value").GetProperty("counts").GetProperty("approved").GetInt32());
    }

    [Fact]
    public async Task Cancel_ReturnsVersionAndReason_RejectsOtherProposerAndCustomer()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Coordinator.AccountId);
        await using var host = await StartAsync(db, f.Storage);
        var url = $"/api/v1/emergency-cost-requests/{cost.EmergencyCostRequestId}";
        var request = new { lastUpdatedAt = cost.UpdatedAt, cancellationReason = Reason };
        using var driver = Client(host, f.Driver.AccountId, f.Driver.Role);
        Assert.Equal(HttpStatusCode.NotFound, (await driver.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PostAsJsonAsync(url + "/cancel", request)).StatusCode);
        using var rc = Client(host, f.Coordinator.AccountId, f.Coordinator.Role);
        using var response = await rc.PostAsJsonAsync(url + "/cancel", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value");
        Assert.Equal("CANCELLED", cancelled.GetProperty("status").GetString()); Assert.Equal(Reason, cancelled.GetProperty("cancellationReason").GetString());
        Assert.True(cancelled.GetProperty("updatedAt").GetDateTimeOffset() > cost.UpdatedAt);
        using var customer = CustomerClient(host, f.CustomerAccountId);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/emergency-cost-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync(url + "/cancel", request)).StatusCode);
        rc.DefaultRequestHeaders.Authorization = null; Assert.Equal(HttpStatusCode.Unauthorized, (await rc.GetAsync(url)).StatusCode);
        Assert.DoesNotContain(await db.AccountNotifications.ToArrayAsync(), row => row.AccountId == f.CustomerAccountId);
    }

    [Theory]
    [InlineData("null")][InlineData("0")][InlineData("-1")][InlineData("1.0")][InlineData("1.5")]
    [InlineData("\"123\"")][InlineData("true")][InlineData("1e100")]
    public async Task Create_RejectsInvalidOrFractionalVndWithoutWrites(string amount)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage); using var driver = Client(host, f.Driver.AccountId, f.Driver.Role);
        var body = $$"""{"category":"TOWING","requestedAmount":{{amount}},"description":"Emergency towing required.","justification":"The engine cannot start safely."}""";
        using var response = await driver.PostAsync($"/api/v1/incidents/{incident.IncidentReportId}/emergency-cost-requests", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Empty(db.EmergencyCostRequests); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Policies_RejectForgedRoles_InvalidFiltersAndEmptyVersions()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        await using var host = await StartAsync(db, f.Storage); var url = $"/api/v1/emergency-cost-requests/{cost.EmergencyCostRequestId}";
        foreach (var actor in new[] { f.Driver, f.Escort, f.Coordinator, f.Specialist })
        {
            using var client = Client(host, actor.AccountId, actor.Role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url + "/decision", Decision(cost))).StatusCode);
        }
        foreach (var actor in new[] { f.Manager, f.Specialist })
        {
            using var client = Client(host, actor.AccountId, actor.Role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/incidents/{incident.IncidentReportId}/emergency-cost-requests", CostRequest())).StatusCode);
        }
        using var forged = Client(host, f.Driver.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync(url + "/decision", Decision(cost))).StatusCode);
        using var manager = Client(host, f.Manager.AccountId, f.Manager.Role);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/v1/emergency-cost-requests?status=1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/v1/emergency-cost-requests?sortBy=wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync(url + "/decision", new { decision = "APPROVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/emergency-cost-requests/{Guid.NewGuid()}")).StatusCode);
        Assert.Single(db.Notifications); Assert.Empty(db.CostApprovalDecisions);
    }
}
