using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.IncidentService.Validator;

using static Pegaxus.Service.Test.HorseHealthLogTests;
using static Pegaxus.Service.Test.IncidentLifecycleTests;
using static Pegaxus.Service.Test.VehicleIncidentTests;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.Service.Test;

public class EmergencyCostTests
{
    [Theory]
    [InlineData(EmployeeRole.DRIVER)]
    [InlineData(EmployeeRole.ESCORT)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR)]
    public async Task Create_PersistsProposerInvoiceAndEvidence_NotifiesOnlyActiveManagers(EmployeeRole role)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f);
        var inactive = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.Account.Status = AccountStatus.INACTIVE;
        await db.SaveChangesAsync(); var actor = new[] { f.Driver, f.Escort, f.Coordinator }.Single(employee => employee.Role == role);
        var request = CostRequest(); request.InvoiceRequired = true; request.PhotoFiles = [Photo()];
        var result = await f.Service.CreateCostAsync(incident.IncidentReportId, request, actor.AccountId);
        Assert.Matches(@"^ECR-\d{8}-\d{4}$", result.RequestCode); Assert.Equal("PENDING", result.Status);
        Assert.Equal(15_000_000, result.RequestedAmount); Assert.Null(result.ApprovedAmount); Assert.Null(result.Decision);
        Assert.Equal("VND", result.Currency); Assert.Equal(actor.EmployeeId, result.RequestedBy.EmployeeId); Assert.True(result.InvoiceRequired);
        Assert.Equal(request.Description.Trim(), result.Description); Assert.Equal(request.Justification.Trim(), result.Justification);
        Assert.Equal(role == EmployeeRole.FLEET_ROUTE_COORDINATOR ? "PENDING" : "REPORTED", result.Incident!.Status);
        Assert.True(incident.UpdatedAt > Scenario.Now); Assert.Single(result.Photos!);
        Assert.Equal(EmergencyReceiptStatus.PENDING, (await db.EmergencyCostRequests.SingleAsync()).ReceiptStatus);
        Assert.Equal(result.EmergencyCostRequestId, (await db.IncidentPhotos.SingleAsync()).EmergencyCostRequestId);
        var alert = await db.AccountNotifications.Include(row => row.Notification).SingleAsync();
        Assert.Equal(f.Manager.AccountId, alert.AccountId); Assert.Equal(NotificationType.EMERGENCY_COST, alert.Notification.NotificationType);
        Assert.Equal(NotificationPriority.HIGH, alert.Notification.Priority); Assert.Equal(result.EmergencyCostRequestId, alert.Notification.ReferenceId);
        Assert.Equal($"/admin/emergency-costs/{result.EmergencyCostRequestId}", alert.Notification.ActionUrl);
        Assert.DoesNotContain(await db.OutboxMessages.ToArrayAsync(), row => row.MessageType == "EMAIL");
        var staff = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(incident.IncidentReportId, f.Manager.AccountId));
        Assert.Empty(staff.Photos); Assert.Single(staff.CostRequests); Assert.Equal(1, staff.PendingCostCount);
        var customer = Assert.IsType<Incidents.CustomerResponse.Incident>(await f.Service.GetAsync(incident.IncidentReportId, f.CustomerAccountId));
        Assert.Empty(customer.Photos);
    }

    [Fact]
    public async Task Requests_HaveNoPerIncidentOrCategoryLimit_ReadsNeverAcceptIncident()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var first = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var second = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        Assert.NotEqual(first.RequestCode, second.RequestCode); Assert.Equal(IncidentStatus.REPORTED, incident.Status);
        var before = incident.UpdatedAt;
        var detail = await f.Service.GetCostAsync(first.EmergencyCostRequestId, f.Driver.AccountId);
        Assert.Equal(new[] { "CANCEL" }, detail.AvailableActions); Assert.Equal(second.EmergencyCostRequestId, Assert.Single(detail.OtherRequests!).EmergencyCostRequestId);
        Assert.Equal(incident.Title, detail.IncidentSummary!.Title); Assert.Equal(f.Segment.RoutePlanId, detail.CurrentRoute!.RoutePlanId);
        Assert.Equal(2, detail.HorseHealthSummary!.Total); Assert.Equal(2, detail.HorseHealthSummary.NoData);
        Assert.Equal(IncidentStatus.REPORTED, incident.Status); Assert.Equal(before, incident.UpdatedAt);
        Assert.Equal(2, (await f.Service.ListCostsAsync(new(), f.Driver.AccountId)).TotalItems);
    }

    [Theory]
    [InlineData("APPROVE", null, null, "APPROVED", 15000000)]
    [InlineData("APPROVE", 10000000L, "A lower quote is sufficient.", "APPROVED", 10000000)]
    [InlineData("REJECT", 99999999L, "Insurance covers this cost.", "REJECTED", 0)]
    public async Task Decide_RecordsOneImmutableDecision_NotifiesActualProposer(string decision, long? amount, string? reason, string status, long expected)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var created = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var request = Decision(created); request.Decision = decision; request.ApprovedAmount = amount; request.DecisionReason = reason;
        var result = await f.Service.DecideCostAsync(created.EmergencyCostRequestId, request, f.Manager.AccountId);
        Assert.Equal(status, result.Status); Assert.Equal(expected, result.ApprovedAmount); Assert.Equal(IncidentStatus.PENDING, incident.Status);
        Assert.Equal(expected, result.Decision!.ApprovedAmount); Assert.Equal(f.Manager.EmployeeId, result.Decision.DecidedBy.EmployeeId);
        Assert.True(result.UpdatedAt > created.UpdatedAt); Assert.Equal(result.UpdatedAt, result.Decision.DecidedAt);
        var alerts = await db.AccountNotifications.Include(row => row.Notification).ToArrayAsync(); Assert.Equal(2, alerts.Length);
        var alert = Assert.Single(alerts, row => row.AccountId == f.Driver.AccountId);
        Assert.Equal(NotificationPriority.HIGH, alert.Notification.Priority); Assert.Equal($"/crew/incidents/{incident.IncidentReportId}", alert.Notification.ActionUrl);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => f.Service.DecideCostAsync(created.EmergencyCostRequestId, request, f.Manager.AccountId));
        Assert.Equal("EMERGENCY_COST_ALREADY_DECIDED", conflict.ErrorCode); Assert.Contains(f.Manager.FullName, conflict.Message); Assert.NotNull(conflict.Value);
        Assert.Single(await db.CostApprovalDecisions.ToArrayAsync()); Assert.Equal(2, await db.Notifications.CountAsync());
        var detail = await f.Service.GetCostAsync(created.EmergencyCostRequestId, f.Manager.AccountId); Assert.Empty(detail.AvailableActions!);
        var staff = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(incident.IncidentReportId, f.Manager.AccountId));
        Assert.Equal(expected, staff.ApprovedTotal); Assert.True(staff.CanResolve);
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(15000001)]
    public async Task Decide_RejectsAmountsOutsideRequest(long amount)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var request = Decision(cost); request.ApprovedAmount = amount; request.DecisionReason = Reason;
        Assert.Equal("AMOUNT_EXCEEDS_REQUESTED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.DecideCostAsync(cost.EmergencyCostRequestId, request, f.Manager.AccountId))).ErrorCode);
        Assert.Empty(db.CostApprovalDecisions); Assert.Equal(EmergencyCostStatus.PENDING, (await db.EmergencyCostRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_OnlyActualProposer_RejectsStaleOrProcessedRequest()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var request = new Incidents.Request.CancelRequest { LastUpdatedAt = cost.UpdatedAt.AddSeconds(-1), CancellationReason = Reason };
        foreach (var actor in new[] { f.Manager, f.Coordinator, f.Escort, f.Specialist })
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CancelCostAsync(cost.EmergencyCostRequestId, request, actor.AccountId));
        Assert.Equal("EMERGENCY_COST_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CancelCostAsync(cost.EmergencyCostRequestId, request, f.Driver.AccountId))).ErrorCode);
        request.LastUpdatedAt = cost.UpdatedAt.AddTicks(9);
        var cancelled = await f.Service.CancelCostAsync(cost.EmergencyCostRequestId, request, f.Driver.AccountId);
        Assert.Equal("CANCELLED", cancelled.Status); Assert.Equal(Reason, cancelled.CancellationReason); Assert.Equal(cancelled.UpdatedAt, cancelled.CancelledAt);
        Assert.Null(cancelled.ApprovedAmount); Assert.Null(cancelled.Decision); Assert.True(cancelled.UpdatedAt > cost.UpdatedAt);
        Assert.Equal(IncidentStatus.REPORTED, incident.Status);
        Assert.Equal(NotificationPriority.NORMAL, (await db.Notifications.OrderBy(item => item.CreatedAt).LastAsync()).Priority);
        Assert.Equal("INVALID_EMERGENCY_COST_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CancelCostAsync(cost.EmergencyCostRequestId, request, f.Driver.AccountId))).ErrorCode);
        Assert.Equal("EMERGENCY_COST_ALREADY_DECIDED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideCostAsync(cost.EmergencyCostRequestId, Decision(cancelled), f.Manager.AccountId))).ErrorCode);
        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Scope_ManagersAndSpecialistsReadAll_CoordinatorsAssignedOnly_CrewOwnOnly()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var driverCost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var escortCost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Escort.AccountId);
        foreach (var actor in new[] { f.Manager, f.Specialist, f.Coordinator })
        {
            Assert.Equal(2, (await f.Service.ListCostsAsync(new(), actor.AccountId)).TotalItems);
            Assert.Single((await f.Service.GetCostAsync(driverCost.EmergencyCostRequestId, actor.AccountId)).OtherRequests!);
        }
        foreach (var actor in new[] { f.Driver, f.Escort })
        {
            Assert.Single((await f.Service.ListCostsAsync(new(), actor.AccountId)).Items);
            var own = actor == f.Driver ? driverCost : escortCost; var other = actor == f.Driver ? escortCost : driverCost;
            Assert.Empty((await f.Service.GetCostAsync(own.EmergencyCostRequestId, actor.AccountId)).OtherRequests!);
            Assert.Equal("EMERGENCY_COST_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
                f.Service.GetCostAsync(other.EmergencyCostRequestId, actor.AccountId))).ErrorCode);
        }
        foreach (var role in new[] { EmployeeRole.DRIVER, EmployeeRole.ESCORT, EmployeeRole.FLEET_ROUTE_COORDINATOR })
        {
            var outsider = await EmployeeAsync(db, role);
            Assert.Empty((await f.Service.ListCostsAsync(new(), outsider.AccountId)).Items);
            await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetCostAsync(driverCost.EmergencyCostRequestId, outsider.AccountId));
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), outsider.AccountId));
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.ListCostsAsync(new(), f.CustomerAccountId));
        foreach (var actor in new[] { f.Manager, f.Specialist })
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), actor.AccountId));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.DecideCostAsync(driverCost.EmergencyCostRequestId, Decision(driverCost), f.Coordinator.AccountId));
        var newPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP-NEW",
            Status = TransportPlanStatus.DRAFT, CreatedAt = Scenario.Now.AddMinutes(1), UpdatedAt = Scenario.Now };
        db.Add(newPlan); await db.SaveChangesAsync();
        Assert.Empty((await f.Service.ListCostsAsync(new(), f.Coordinator.AccountId)).Items);
    }

    [Theory]
    [InlineData(IncidentStatus.RESOLVED)][InlineData(IncidentStatus.CANCELLED)]
    public async Task ClosedIncident_RefusesCreationAndDecision(IncidentStatus status)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        incident.Status = status; await db.SaveChangesAsync();
        Assert.Equal("INVALID_INCIDENT_STATUS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId))).ErrorCode);
        Assert.Equal("INVALID_INCIDENT_STATUS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.DecideCostAsync(cost.EmergencyCostRequestId, Decision(cost), f.Manager.AccountId))).ErrorCode);
        Assert.Empty((await f.Service.GetCostAsync(cost.EmergencyCostRequestId, f.Manager.AccountId)).AvailableActions!);
        Assert.Empty(db.CostApprovalDecisions); Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Create_RequiresInTransit_DecisionChecksVersionAndReductionReason()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var incident = AddIncident(db, f); await db.SaveChangesAsync();
        var cost = await f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId);
        var request = Decision(cost); request.LastUpdatedAt = cost.UpdatedAt.AddSeconds(-1);
        Assert.Equal("EMERGENCY_COST_MODIFIED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.DecideCostAsync(cost.EmergencyCostRequestId, request, f.Manager.AccountId))).ErrorCode);
        request.LastUpdatedAt = cost.UpdatedAt; request.ApprovedAmount = cost.RequestedAmount - 1;
        await Assert.ThrowsAsync<FieldValidationException>(() => f.Service.DecideCostAsync(cost.EmergencyCostRequestId, request, f.Manager.AccountId));
        f.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.CreateCostAsync(incident.IncidentReportId, CostRequest(), f.Driver.AccountId))).ErrorCode);
        request.ApprovedAmount = null;
        Assert.Equal("APPROVED", (await f.Service.DecideCostAsync(cost.EmergencyCostRequestId, request, f.Manager.AccountId)).Status);
    }

    [Fact]
    public void Validators_RejectInvalidMoneyTextPhotosFiltersAndVersions()
    {
        var create = new CreateCostRequestValidator(); Assert.True(create.Validate(CostRequest()).IsValid);
        foreach (var amount in new decimal?[] { null, 0, -1, 1.1m, 1.0m, 10_000_000_000_000_000m })
        { var request = CostRequest(); request.RequestedAmount = amount; Assert.False(create.Validate(request).IsValid); }
        foreach (var field in new[] { "category", "description", "justification", "photos" })
        {
            var request = CostRequest();
            if (field == "category") request.Category = "1";
            if (field == "description") request.Description = " 123456789 ";
            if (field == "justification") request.Justification = "short";
            if (field == "photos") { var photo = Photo(); request.PhotoFiles = [photo, photo]; }
            Assert.False(create.Validate(request).IsValid);
        }
        var decide = new CostDecisionRequestValidator();
        Assert.False(decide.Validate(new Incidents.Request.CostDecisionRequest { Decision = "APPROVE" }).IsValid);
        Assert.False(decide.Validate(new Incidents.Request.CostDecisionRequest { LastUpdatedAt = DateTimeOffset.UtcNow, Decision = "REJECT", DecisionReason = "short" }).IsValid);
        Assert.True(decide.Validate(new Incidents.Request.CostDecisionRequest { LastUpdatedAt = DateTimeOffset.UtcNow, Decision = "REJECT", ApprovedAmount = -1.5m, DecisionReason = Reason }).IsValid);
        var list = new CostListRequestValidator();
        foreach (var request in new Incidents.Request.CostListRequest[] { new() { Status = "1" }, new() { Status = "PENDING," }, new() { Category = "BAD" },
            new() { PageIndex = 0 }, new() { PageSize = 101 }, new() { PageIndex = int.MaxValue }, new() { TransportId = Guid.Empty },
            new() { SortBy = "requestedAt:other" }, new() { FromDate = new(2026, 10, 22), ToDate = new(2026, 10, 21) } })
            Assert.False(list.Validate(request).IsValid);
    }

    internal static Incidents.Request.CreateCostRequest CostRequest() => new()
    {
        Category = "TOWING", RequestedAmount = 15_000_000, Description = "  Specialised tow truck for the horse truck.  ",
        Justification = "  The horses must leave the hard shoulder safely.  "
    };
    internal static Incidents.Request.CostDecisionRequest Decision(Incidents.Response.CostRequest cost) => new() { LastUpdatedAt = cost.UpdatedAt, Decision = "APPROVE" };
}
