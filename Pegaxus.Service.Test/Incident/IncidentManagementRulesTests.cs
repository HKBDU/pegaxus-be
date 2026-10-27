using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

/// <summary>Who may lower a severity or cancel a mistaken report, and when a broken vehicle is taken out of service.</summary>
public class IncidentManagementRulesTests
{
    private static Incidents.Request.RaiseSeverityRequest Change(IncidentReport item, string severity) => new() { LastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), Severity = severity, Reason = Reason };
    private static Incidents.Request.CancelRequest Cancel(IncidentReport item) => new() { LastUpdatedAt = DbTime.AsUtc(item.UpdatedAt), CancellationReason = Reason };

    private static Employee Pick(Scenario f, EmployeeRole role) => new[] { f.Manager, f.Coordinator, f.Driver, f.Escort }.Single(employee => employee.Role == role);

    [Fact]
    public async Task Lower_ByTheManager_KeepsTheStatus_LogsTheReason_AndTellsOnlyStaffInTheApp()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f, IncidentStatus.PENDING, IncidentSeverity.CRITICAL); await db.SaveChangesAsync();

        var result = await f.Service.RaiseSeverityAsync(item.IncidentReportId, Change(item, "MEDIUM"), f.Manager.AccountId);

        Assert.Equal("MEDIUM", result.Value.Severity); Assert.Equal("CRITICAL", result.PreviousSeverity); Assert.Equal("PENDING", result.Value.Status);
        Assert.Contains(f.Manager.FullName, result.Value.Description); Assert.Contains(Reason, result.Value.Description);
        Assert.False(result.Value.Effects!.CustomerNotified); Assert.False(result.Value.Effects.EmailQueued);
        var alerts = await db.AccountNotifications.Include(row => row.Notification).ToListAsync();
        Assert.Equal(new[] { f.Coordinator.AccountId, f.Specialist.AccountId }.Order(), alerts.Select(row => row.AccountId).Order());
        Assert.All(alerts, row => { Assert.Equal(NotificationPriority.NORMAL, row.Notification.Priority); Assert.Contains("severity lowered", row.Notification.Title); });
        Assert.DoesNotContain(alerts, row => row.AccountId == f.CustomerAccountId);
    }

    [Theory]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Lower_ByAnyoneButTheManager_IsRefused(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f, IncidentStatus.PENDING, IncidentSeverity.HIGH); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.RaiseSeverityAsync(item.IncidentReportId, Change(item, "LOW"), Pick(f, role).AccountId));

        Assert.Equal("SEVERITY_LOWER_MANAGER_ONLY", error.ErrorCode); Assert.Equal(IncidentSeverity.HIGH, item.Severity);
    }

    [Theory]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    public async Task Cancel_ByAnAssignedCrew_BeforeTheManagerTakesItUp_CancelsTheReportAndTellsTheStaff(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f); await db.SaveChangesAsync(); var actor = Pick(f, role);

        var result = await f.Service.CancelAsync(item.IncidentReportId, Cancel(item), actor.AccountId);

        Assert.Equal("CANCELLED", result.Status); Assert.Equal(Reason, result.CancellationReason);
        var alerts = await db.AccountNotifications.Include(row => row.Notification).ToListAsync();
        Assert.Contains(alerts, row => row.AccountId == f.Manager.AccountId && row.Notification.Message.Contains("crew cancelled the incident report"));
        Assert.Contains(alerts, row => row.AccountId == f.Coordinator.AccountId); Assert.Contains(alerts, row => row.AccountId == f.Specialist.AccountId);
        Assert.DoesNotContain(alerts, row => row.AccountId == actor.AccountId);
    }

    [Fact]
    public async Task Cancel_ByTheCrew_IsRefusedOnceTheManagerHasTakenTheIncidentUp()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f, IncidentStatus.PENDING); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CancelAsync(item.IncidentReportId, Cancel(item), f.Driver.AccountId));

        Assert.Equal("INVALID_INCIDENT_STATUS", error.ErrorCode); Assert.Equal(IncidentStatus.PENDING, item.Status);
    }

    [Theory]
    [InlineData(EmergencyCostStatus.PENDING)]
    [InlineData(EmergencyCostStatus.APPROVED)]
    public async Task Cancel_ByTheCrew_IsRefusedWhileACostIsOpen(EmergencyCostStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f); AddCost(db, item, f.Driver, status); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => f.Service.CancelAsync(item.IncidentReportId, Cancel(item), f.Escort.AccountId));

        Assert.Equal("HAS_OPEN_EMERGENCY_COSTS", error.ErrorCode); Assert.Equal(IncidentStatus.REPORTED, item.Status);
    }

    [Fact]
    public async Task Cancel_IsRefusedForTheCoordinator_AndForACrewNotAssignedToTheTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f); await db.SaveChangesAsync(); var stranger = await EmployeeAsync(db, EmployeeRole.DRIVER);

        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CancelAsync(item.IncidentReportId, Cancel(item), f.Coordinator.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CancelAsync(item.IncidentReportId, Cancel(item), stranger.AccountId));
        Assert.Equal(IncidentStatus.REPORTED, item.Status);
    }

    private static async Task<(Incidents.Response.Incident Incident, EmergencyCostRequest Cost)> ReportWithCostAsync(Microsoft.EntityFrameworkCore.DbContext db, Scenario f,
        bool operable, bool replacementRequired, EmergencyCostCategory category)
    {
        var report = f.Report(); report.VehicleOperable = operable; report.ReplacementVehicleRequired = replacementRequired; report.PhotoFiles = [Photo()];
        var incident = (await f.Service.ReportVehicleAsync(report, f.Driver.AccountId)).Value;
        var entity = await ((Pegaxus.Repository.AppDbContext)db).IncidentReports.SingleAsync(item => item.IncidentReportId == incident.IncidentReportId);
        var cost = AddCost((Pegaxus.Repository.AppDbContext)db, entity, f.Driver, EmergencyCostStatus.PENDING);
        cost.Category = category; cost.ApprovedAmount = null; await db.SaveChangesAsync();
        return (incident, cost);
    }

    private static Task<Incidents.Response.CostRequest> DecideAsync(Scenario f, EmergencyCostRequest cost, string decision) =>
        f.Service.DecideCostAsync(cost.EmergencyCostRequestId, new() { LastUpdatedAt = DbTime.AsUtc(cost.UpdatedAt), Decision = decision, DecisionReason = decision == "REJECT" ? "Insurance covers this cost." : null }, f.Manager.AccountId);

    [Theory]
    [InlineData(EmergencyCostCategory.REPLACEMENT_VEHICLE, "APPROVE", false, true, true)]
    [InlineData(EmergencyCostCategory.TOWING, "APPROVE", false, true, false)]
    [InlineData(EmergencyCostCategory.REPLACEMENT_VEHICLE, "REJECT", false, true, false)]
    [InlineData(EmergencyCostCategory.REPLACEMENT_VEHICLE, "APPROVE", true, false, false)]
    [InlineData(EmergencyCostCategory.REPLACEMENT_VEHICLE, "APPROVE", false, false, false)]
    public async Task Vehicle_IsOnlyTakenOutOfService_WhenAReplacementCostIsApprovedForAVehicleThatCannotRun(
        EmergencyCostCategory category, string decision, bool operable, bool replacementRequired, bool locked)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var (_, cost) = await ReportWithCostAsync(db, f, operable, replacementRequired, category);
        Assert.Equal(VehicleStatus.IN_USE, f.Vehicle.Status);

        await DecideAsync(f, cost, decision);

        Assert.Equal(locked ? VehicleStatus.OUT_OF_SERVICE : VehicleStatus.IN_USE, f.Vehicle.Status);
    }

    [Fact]
    public async Task AMistakenReport_CancelledByTheCrew_NeverLocksTheVehicle()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var report = f.Report(); report.VehicleOperable = false; report.ReplacementVehicleRequired = true; report.PhotoFiles = [Photo()];
        var incident = (await f.Service.ReportVehicleAsync(report, f.Driver.AccountId)).Value;

        var cancelled = await f.Service.CancelAsync(incident.IncidentReportId, new() { LastUpdatedAt = incident.UpdatedAt, CancellationReason = Reason }, f.Escort.AccountId);

        Assert.Equal("CANCELLED", cancelled.Status); Assert.Equal(VehicleStatus.IN_USE, f.Vehicle.Status);
    }
}
