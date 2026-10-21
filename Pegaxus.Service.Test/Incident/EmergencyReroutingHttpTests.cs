using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pegaxus.API.Services;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Test.IncidentLifecycleHttpTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentHttpTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

namespace Pegaxus.Service.Test;

public class EmergencyReroutingHttpTests
{
    [Fact]
    public async Task ExpiryWorker_ProcessesOverdueHoldOnStartup_UsingRegisteredScopedService()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        item.CreatedAt = item.ReportedAt = Scenario.Now.AddHours(-1); await db.SaveChangesAsync();
        var earlier = new EmergencyReroutingTests.MutableClock { Now = DbTime.AsUtc(Scenario.Now.AddMinutes(-16)) };
        await CreateService(db, f.Storage, earlier).HoldRouteAsync(item.IncidentReportId, EmergencyReroutingTests.HoldRequest(), f.Manager.AccountId);
        await using var host = await StartAsync(db, f.Storage);
        using var worker = new IncidentHoldExpiryWorker(host.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<IncidentHoldExpiryWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.Single(await db.Notifications.Where(row => row.ReferenceType == "IncidentHoldExpired").ToArrayAsync());
            Assert.Single(await db.OutboxMessages.Where(row => row.MessageType == "EMAIL").ToArrayAsync());
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task RerouteEndpoints_ReturnContractEnvelopes_AndDetailProjectsPhoneFreeHold()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        await using var host = await StartAsync(db, f.Storage);
        using var rc = Client(host, f.Coordinator.AccountId, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        using var lm = Client(host, f.Manager.AccountId, EmployeeRole.LOGISTICS_MANAGER);
        var url = $"/api/v1/incidents/{item.IncidentReportId}";
        var context = await rc.GetFromJsonAsync<JsonElement>(url + "/reroute-context");
        Assert.Equal("Reroute context retrieved successfully", context.GetProperty("message").GetString());
        Assert.True(context.GetProperty("isSuccess").GetBoolean()); Assert.Equal(200, context.GetProperty("code").GetInt32());
        Assert.Equal("REPORTED", context.GetProperty("value").GetProperty("incident").GetProperty("status").GetString());
        using var escalation = await rc.PostAsJsonAsync(url + "/reroute-escalation", new { reason = Reason, suggestedAction = "HOLD", suggestedHoldMinutes = 15 });
        Assert.Equal(HttpStatusCode.OK, escalation.StatusCode);
        var escalated = await escalation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Escalation sent to the logistics manager", escalated.GetProperty("message").GetString());
        Assert.Equal("PENDING", escalated.GetProperty("value").GetProperty("incidentStatus").GetString());
        Assert.Single(escalated.GetProperty("value").GetProperty("notifiedLogisticsManagers").EnumerateArray());
        using var hold = await lm.PostAsJsonAsync(url + "/route-hold", new { holdMinutes = 15, reason = Reason });
        Assert.Equal(HttpStatusCode.OK, hold.StatusCode); var held = await hold.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Transport put on hold successfully", held.GetProperty("message").GetString());
        var value = held.GetProperty("value"); Assert.Equal("ACTIVE", value.GetProperty("hold").GetProperty("status").GetString());
        Assert.Equal(30, value.GetProperty("hold").GetProperty("monitorIntervalMinutes").GetInt32());
        Assert.Equal(4, value.GetProperty("notified").GetArrayLength()); AssertNoProperties(value, ["phone"]);
        using var customer = CustomerClient(host, f.CustomerAccountId);
        var customerDetail = await customer.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal("ACTIVE", customerDetail.GetProperty("value").GetProperty("hold").GetProperty("status").GetString());
        AssertNoProperties(customerDetail.GetProperty("value"), ["phone", "availableActions", "costRequests"]);
        using var duplicate = await lm.PostAsJsonAsync(url + "/route-hold", new { holdMinutes = 15, reason = Reason });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("HOLD_ALREADY_ACTIVE", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.DoesNotContain(await db.AccountNotifications.ToArrayAsync(), row => row.AccountId == f.CustomerAccountId);
    }

    [Fact]
    public async Task RerouteEndpoints_EnforcePoliciesAssignmentAndRequestValidation()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var unrelated = await HorseHealthLogTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        await using var host = await StartAsync(db, f.Storage); var url = $"/api/v1/incidents/{item.IncidentReportId}";
        foreach (var employee in new[] { f.Specialist, f.Driver, f.Escort })
        {
            using var client = Client(host, employee.AccountId, employee.Role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url + "/reroute-context")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url + "/reroute-escalation", new { reason = Reason })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url + "/route-hold", new { holdMinutes = 15, reason = Reason })).StatusCode);
        }
        using var other = Client(host, unrelated.AccountId, unrelated.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(url + "/reroute-context")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(url + "/reroute-escalation", new { reason = Reason })).StatusCode);
        using var rc = Client(host, f.Coordinator.AccountId, f.Coordinator.Role); using var lm = Client(host, f.Manager.AccountId, f.Manager.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await rc.PostAsJsonAsync(url + "/route-hold", new { holdMinutes = 15, reason = Reason })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lm.PostAsJsonAsync(url + "/reroute-escalation", new { reason = Reason })).StatusCode);
        foreach (var body in new object[] { new { reason = Reason }, new { holdMinutes = 14, reason = Reason },
            new { holdMinutes = 15, reason = "short" }, new { holdMinutes = 15, reason = Reason, monitorIntervalMinutes = 121 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await lm.PostAsJsonAsync(url + "/route-hold", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await rc.PostAsJsonAsync(url + "/reroute-escalation", new { reason = Reason, blockedRoutePlanIds = new[] { "bad-id" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await lm.GetAsync($"/api/v1/incidents/{Guid.NewGuid()}/reroute-context")).StatusCode);
        Assert.Equal(IncidentStatus.REPORTED, item.Status); Assert.Empty(db.Notifications);
    }
}
