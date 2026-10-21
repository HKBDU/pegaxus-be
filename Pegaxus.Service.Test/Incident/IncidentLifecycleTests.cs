using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

public class IncidentLifecycleTests
{
    internal const string Summary = "The crew completed repairs and checked the horses safely.";
    internal const string Reason = "The original report needs a documented correction.";

    [Theory]
    [InlineData("resolve")]
    [InlineData("cancel")]
    [InlineData("self-resolve")]
    public async Task Close_PreservesSeverityVehicleAndEvidence_NotifiesOnlyExpectedRecipients(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var report = f.Report(); report.VehicleOperable = false; report.ReplacementVehicleRequired = true;
        report.PhotoFiles = [Photo()]; var created = (await f.Service.ReportVehicleAsync(report, f.Driver.AccountId)).Value;
        var item = await db.IncidentReports.SingleAsync(); item.EmergencySettlementRequired = true; await db.SaveChangesAsync();
        var priorIds = await db.AccountNotifications.Select(row => row.NotificationId).ToArrayAsync();
        var emailCount = await db.OutboxMessages.CountAsync(row => row.MessageType == "EMAIL");
        var result = await ApplyAsync(f.Service, item, action, action == "self-resolve" ? f.Escort.AccountId : f.Manager.AccountId);
        Assert.Equal(action == "cancel" ? "CANCELLED" : "RESOLVED", result.Status);
        Assert.Equal(created.Severity, result.Severity); Assert.Equal(created.Description, result.Description);
        Assert.Equal("OUT_OF_SERVICE", result.Vehicle!.Status); Assert.Equal(created.Photos, result.Photos);
        Assert.Empty(result.AvailableActions); Assert.False(result.CanResolve); Assert.True(result.UpdatedAt > created.UpdatedAt);
        Assert.Equal(action == "cancel", result.EmergencySettlementRequired);
        Assert.Equal(action == "cancel" ? null : Summary, result.ResolutionSummary);
        Assert.Equal(action == "cancel" ? Reason : null, result.CancellationReason);
        var alerts = await db.AccountNotifications.Include(row => row.Notification).Where(row => !priorIds.Contains(row.NotificationId)).ToArrayAsync();
        var expected = action == "self-resolve"
            ? new[] { f.Manager.AccountId, f.Coordinator.AccountId, f.Specialist.AccountId, f.CustomerAccountId }
            : new[] { f.Coordinator.AccountId, f.Specialist.AccountId, f.Driver.AccountId, f.Escort.AccountId, f.CustomerAccountId };
        Assert.Equal(expected.Order(), alerts.Select(row => row.AccountId).Order());
        Assert.All(alerts, row => Assert.Equal(action == "self-resolve" && row.AccountId != f.CustomerAccountId
            ? NotificationPriority.HIGH : NotificationPriority.NORMAL, row.Notification.Priority));
        foreach (var crewId in new[] { f.Driver.AccountId, f.Escort.AccountId })
            Assert.All(alerts.Where(row => row.AccountId == crewId), row => Assert.Equal($"/crew/incidents/{item.IncidentReportId}", row.Notification.ActionUrl));
        var customer = Assert.Single(alerts.Where(row => row.AccountId == f.CustomerAccountId)).Notification;
        foreach (var secret in new[] { report.Title, report.Description, f.Driver.FullName, f.Location.LocationName, Summary, Reason })
            Assert.DoesNotContain(secret, customer.Title + customer.Message);
        Assert.Equal(emailCount, await db.OutboxMessages.CountAsync(row => row.MessageType == "EMAIL"));
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("cancel")]
    [InlineData("self-resolve")]
    public async Task Close_NeverChangesHistoricalMedicalLogs(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await HorseIncidentTests.Scenario.CreateAsync(db);
        await f.Service.ReportHorseHealthAsync(f.Report(), f.Escort.AccountId);
        var before = await db.HorseHealthLogs.AsNoTracking().Select(log => new { log.HorseHealthLogId, log.GeneralNote, log.TemperatureCelsius, log.CheckedAt }).ToArrayAsync();
        var item = await db.IncidentReports.SingleAsync();
        await ApplyAsync(f.Service, item, action, action == "self-resolve" ? f.Driver.AccountId : f.Manager.AccountId);
        var after = await db.HorseHealthLogs.AsNoTracking().Select(log => new { log.HorseHealthLogId, log.GeneralNote, log.TemperatureCelsius, log.CheckedAt }).ToArrayAsync();
        Assert.Equal(before, after); Assert.Single(after); Assert.Single(await db.HorseHealthIncidentDetails.ToArrayAsync());
    }

    [Fact]
    public async Task Resolve_BlocksPendingCosts_ReturnsStructuredValue_ThenClosesCompletedTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var pending = AddCost(db, item, f.Driver, EmergencyCostStatus.PENDING);
        AddCost(db, item, f.Escort, EmergencyCostStatus.APPROVED); AddCost(db, item, f.Driver, EmergencyCostStatus.REJECTED);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => ApplyAsync(f.Service, item, "resolve", f.Manager.AccountId));
        Assert.Equal("UNRESOLVED_EMERGENCY_COSTS", error.ErrorCode);
        var value = JsonSerializer.SerializeToElement(error.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var blocked = Assert.Single(value.GetProperty("pendingRequests").EnumerateArray());
        Assert.Equal(pending.EmergencyCostRequestId, blocked.GetProperty("emergencyCostRequestId").GetGuid());
        Assert.Equal(1000, blocked.GetProperty("requestedAmount").GetInt64());
        Assert.Equal(IncidentStatus.REPORTED, item.Status); Assert.Empty(db.Notifications);
        pending.Status = EmergencyCostStatus.REJECTED; f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var result = await ApplyAsync(f.Service, item, "resolve", f.Manager.AccountId);
        Assert.Equal("RESOLVED", result.Status); Assert.Equal(700, result.CostSummary.ApprovedTotal);
        Assert.Equal(3000, result.CostSummary.RequestedTotal); Assert.Equal(0, result.PendingCostCount);
    }

    [Fact]
    public async Task Cancel_BlocksApprovedCosts_AutoCancelsOnlyPending_WithDeduplicatedRequesterAlerts()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var requester = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var pending = AddCost(db, item, requester, EmergencyCostStatus.PENDING); AddCost(db, item, requester, EmergencyCostStatus.PENDING);
        var approved = AddCost(db, item, f.Escort, EmergencyCostStatus.APPROVED); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => ApplyAsync(f.Service, item, "cancel", f.Manager.AccountId));
        Assert.Equal("HAS_APPROVED_EMERGENCY_COST", error.ErrorCode);
        Assert.Contains(approved.RequestCode, JsonSerializer.Serialize(error.Value)); Assert.Equal(EmergencyCostStatus.PENDING, pending.Status);
        approved.Status = EmergencyCostStatus.REJECTED; await db.SaveChangesAsync();
        var result = await ApplyAsync(f.Service, item, "cancel", f.Manager.AccountId);
        Assert.Equal(2, result.CostRequests.Count(cost => cost.Status == "CANCELLED")); Assert.Equal(EmergencyCostStatus.REJECTED, approved.Status);
        Assert.Equal(Reason, pending.CancellationReason); Assert.Equal(item.CancelledAt, pending.CancelledAt); Assert.Equal(item.UpdatedAt, pending.UpdatedAt);
        Assert.Equal(1, await db.AccountNotifications.CountAsync(row => row.AccountId == requester.AccountId));
        Assert.Equal(1000, result.CostSummary.RequestedTotal); Assert.Equal(0, result.PendingCostCount);
    }

    [Theory]
    [InlineData(EmergencyCostStatus.PENDING, false)]
    [InlineData(EmergencyCostStatus.APPROVED, false)]
    [InlineData(EmergencyCostStatus.REJECTED, true)]
    [InlineData(EmergencyCostStatus.CANCELLED, true)]
    public async Task SelfResolve_RequiresNoPendingOrApprovedCost(EmergencyCostStatus status, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var cost = AddCost(db, item, f.Driver, status); await db.SaveChangesAsync();
        if (allowed) Assert.Equal("RESOLVED", (await ApplyAsync(f.Service, item, "self-resolve", f.Escort.AccountId)).Status);
        else
        {
            var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => ApplyAsync(f.Service, item, "self-resolve", f.Escort.AccountId));
            Assert.Equal("HAS_OPEN_EMERGENCY_COSTS", error.ErrorCode); Assert.Contains(cost.RequestCode, JsonSerializer.Serialize(error.Value));
            Assert.Empty(db.Notifications);
        }
    }

    [Theory]
    [InlineData("resolve", IncidentStatus.RESOLVED)]
    [InlineData("resolve", IncidentStatus.CANCELLED)]
    [InlineData("cancel", IncidentStatus.RESOLVED)]
    [InlineData("cancel", IncidentStatus.CANCELLED)]
    [InlineData("self-resolve", IncidentStatus.PENDING)]
    [InlineData("self-resolve", IncidentStatus.RESOLVED)]
    [InlineData("self-resolve", IncidentStatus.CANCELLED)]
    [InlineData("raise-severity", IncidentStatus.RESOLVED)]
    [InlineData("raise-severity", IncidentStatus.CANCELLED)]
    public async Task Lifecycle_RejectsInvalidStatusesWithoutEffects(string action, IncidentStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f, status); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => ApplyAsync(f.Service, item, action,
            action == "self-resolve" ? f.Driver.AccountId : f.Manager.AccountId));
        Assert.Equal("INVALID_INCIDENT_STATUS", error.ErrorCode); Assert.Equal(status, item.Status); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("resolve", EmployeeRole.DRIVER, true)]
    [InlineData("cancel", EmployeeRole.FLEET_ROUTE_COORDINATOR, true)]
    [InlineData("self-resolve", EmployeeRole.LOGISTICS_MANAGER, true)]
    [InlineData("self-resolve", EmployeeRole.DRIVER, false)]
    [InlineData("raise-severity", EmployeeRole.TRANSPORT_SPECIALIST, true)]
    [InlineData("raise-severity", EmployeeRole.FLEET_ROUTE_COORDINATOR, false)]
    [InlineData("raise-severity", EmployeeRole.DRIVER, false)]
    public async Task Lifecycle_EnforcesRolesAndCurrentAssignment(string action, EmployeeRole role, bool assigned)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var actor = assigned ? new[] { f.Manager, f.Coordinator, f.Specialist, f.Driver, f.Escort }.Single(employee => employee.Role == role)
            : await EmployeeAsync(db, role);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() => ApplyAsync(f.Service, item, action, actor.AccountId))).ErrorCode);
        Assert.Empty(db.Notifications); Assert.Equal(IncidentSeverity.HIGH, item.Severity);
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("cancel")]
    [InlineData("self-resolve")]
    [InlineData("raise-severity")]
    public async Task Lifecycle_RejectsStaleVersions(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        Assert.Equal("INCIDENT_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() => ApplyAsync(f.Service, item, action,
            action == "self-resolve" ? f.Driver.AccountId : f.Manager.AccountId, DbTime.AsUtc(item.UpdatedAt.AddSeconds(-1))))).ErrorCode);
        Assert.Empty(db.Notifications); Assert.Equal(IncidentStatus.REPORTED, item.Status);
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("self-resolve")]
    public async Task Resolve_RejectsInvalidTransportStatus(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        f.Transport.Status = TransportStatus.CANCELLED; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => ApplyAsync(f.Service, item, action,
            action == "self-resolve" ? f.Driver.AccountId : f.Manager.AccountId))).ErrorCode); Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Raise_OnlyIncreasesSeverity_AppendsAudit_PreservesStatus_NotifiesEachHighEvent(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f, IncidentStatus.PENDING, IncidentSeverity.LOW);
        await db.SaveChangesAsync(); var actor = new[] { f.Manager, f.Coordinator, f.Driver, f.Escort }.Single(employee => employee.Role == role);
        var initial = item.Description;
        foreach (var severity in new[] { "MEDIUM", "HIGH", "CRITICAL" })
        {
            var result = await f.Service.RaiseSeverityAsync(item.IncidentReportId, new() { LastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), Severity = severity, Reason = Reason }, actor.AccountId);
            Assert.Equal(severity, result.Value.Severity); Assert.Equal("PENDING", result.Value.Status);
            Assert.StartsWith(initial, result.Value.Description); Assert.Contains(actor.FullName, result.Value.Description); Assert.Contains(Reason, result.Value.Description);
            Assert.Equal(severity != "MEDIUM", result.Value.Effects!.CustomerNotified);
            Assert.Equal(severity != "MEDIUM", result.Value.Effects.EmailQueued);
            Assert.Equal(severity == "MEDIUM" ? "NORMAL" : "URGENT", result.Value.Effects.NotifiedPriority);
        }
        var customer = await db.AccountNotifications.Include(row => row.Notification).Where(row => row.AccountId == f.CustomerAccountId).ToArrayAsync();
        Assert.Equal(2, customer.Length); Assert.All(customer, row => Assert.Equal(NotificationPriority.HIGH, row.Notification.Priority));
        Assert.Equal(3, await db.AccountNotifications.CountAsync(row => row.AccountId == f.Specialist.AccountId));
        Assert.Equal(2, await db.OutboxMessages.CountAsync(row => row.MessageType == "EMAIL"));
        foreach (var severity in new[] { "CRITICAL", "HIGH", "LOW" })
        {
            var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.RaiseSeverityAsync(item.IncidentReportId,
                new() { LastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), Severity = severity, Reason = Reason }, actor.AccountId));
            Assert.Equal(severity == "CRITICAL" ? "SEVERITY_UNCHANGED" : "SEVERITY_CANNOT_DECREASE", error.ErrorCode);
        }
        Assert.Equal(11, await db.Notifications.CountAsync());
    }

    [Theory]
    [InlineData("resolve")]
    [InlineData("cancel")]
    [InlineData("self-resolve")]
    public async Task LowSeverityClose_DoesNotNotifyCustomer(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f, severity: IncidentSeverity.LOW); await db.SaveChangesAsync();
        await ApplyAsync(f.Service, item, action, action == "self-resolve" ? f.Driver.AccountId : f.Manager.AccountId);
        Assert.False(await db.AccountNotifications.AnyAsync(row => row.AccountId == f.CustomerAccountId));
        Assert.All(db.Notifications, notification => Assert.Equal(NotificationPriority.NORMAL, notification.Priority));
    }

    internal static IncidentReport AddIncident(AppDbContext db, Scenario f, IncidentStatus status = IncidentStatus.REPORTED,
        IncidentSeverity severity = IncidentSeverity.HIGH, Employee? reporter = null)
    {
        var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), IncidentCode = "INC-" + Guid.NewGuid().ToString("N"),
            TransportId = f.Transport.TransportId, Employee = reporter ?? f.Driver, Location = f.Location, Category = IncidentCategory.VEHICLE,
            Severity = severity, Status = status, Title = "Engine inspection", Description = "A private detailed incident description.",
            OccurredAt = Scenario.Now.AddMinutes(-1), ReportedAt = Scenario.Now, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now,
            EmergencySettlementRequired = true };
        db.Add(incident); return incident;
    }

    internal static EmergencyCostRequest AddCost(AppDbContext db, IncidentReport incident, Employee requester, EmergencyCostStatus status)
    {
        var cost = new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReport = incident,
            RequestedByEmployee = requester, RequestCode = "ECR-" + Guid.NewGuid().ToString("N"), Category = EmergencyCostCategory.TOWING,
            Description = "Private towing service", Justification = "Transport cannot continue safely", Currency = Vnd.Currency,
            Status = status, RequestedAmount = 1000, ApprovedAmount = status == EmergencyCostStatus.APPROVED ? 700 : null,
            RequestedAt = Scenario.Now, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now };
        db.Add(cost); return cost;
    }

    internal static async Task<Incidents.Response.Incident> ApplyAsync(Incidents.Service service, IncidentReport item, string action, Guid accountId, DateTimeOffset? version = null)
    {
        var updatedAt = version ?? DbTime.AsUtc(item.UpdatedAt);
        return action switch
        {
            "resolve" => await service.ResolveAsync(item.IncidentReportId, new() { LastUpdatedAt = updatedAt, ResolutionSummary = Summary, EmergencySettlementConfirmed = true }, accountId),
            "cancel" => await service.CancelAsync(item.IncidentReportId, new() { LastUpdatedAt = updatedAt, CancellationReason = Reason }, accountId),
            "self-resolve" => await service.SelfResolveAsync(item.IncidentReportId, new() { LastUpdatedAt = updatedAt, ResolutionSummary = Summary }, accountId),
            _ => (await service.RaiseSeverityAsync(item.IncidentReportId, new() { LastUpdatedAt = updatedAt, Severity = "CRITICAL", Reason = Reason }, accountId)).Value
        };
    }
}
