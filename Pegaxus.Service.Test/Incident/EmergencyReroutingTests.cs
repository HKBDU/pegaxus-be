using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
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

public class EmergencyReroutingTests
{
    [Fact]
    public async Task Context_IsReadOnly_UsesEffectiveSegmentsAndCurrentPlans_WithRoleSpecificActions()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db);
        var item = AddIncident(db, f); var primary = f.Segment.RoutePlan;
        var remaining = PersonnelAssignmentTests.Segment(primary, 2); db.Add(remaining);
        remaining.PlannedDepartureAt = Scenario.Now.AddHours(4); remaining.PlannedArrivalAt = Scenario.Now.AddHours(5);
        var backup = AddPlan(db, f, RouteType.BACKUP); var cancelled = AddPlan(db, f, RouteType.BACKUP, RoutePlanStatus.CANCELLED);
        var mixedPrefix = PersonnelAssignmentTests.Segment(backup, 1); db.Add(mixedPrefix);
        mixedPrefix.PlannedDepartureAt = Scenario.Now; mixedPrefix.PlannedArrivalAt = Scenario.Now.AddHours(1);
        var main = AddDossier(db, f, DossierType.MAIN); await db.SaveChangesAsync();
        var version = item.UpdatedAt;
        var rc = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId);
        Assert.Equal(primary.TransportPlanId, rc.Transport.TransportPlanId);
        Assert.Equal(f.Segment.RoutePlanRouteId, rc.CurrentSegment!.RoutePlanRouteId); Assert.Equal("IN_TRANSIT", rc.CurrentSegment.Status);
        Assert.Equal(DbTime.AsUtc(f.Segment.ActualDepartureAt), rc.CurrentSegment.ActualDepartureAt); Assert.Null(rc.CurrentSegment.ActualArrivalAt);
        Assert.Equal(f.Location.LocationId, rc.CurrentSegment.FromLocation!.LocationId);
        Assert.Equal(remaining.RoutePlanRouteId, Assert.Single(rc.RemainingSegments).RoutePlanRouteId);
        var option = Assert.Single(rc.BackupPlans); Assert.Equal(backup.RoutePlanId, option.RoutePlanId);
        Assert.True(option.IsAvailable); Assert.Null(option.UnavailableReason); Assert.Equal(2, option.BranchSequenceNo);
        Assert.All(option.Checks, check => Assert.True(check.Passed)); Assert.Null(rc.ReturnPlan); Assert.Null(rc.Hold);
        var expectedDelay = (decimal)(backup.RoutePlanRoutes.Max(segment => segment.PlannedArrivalAt)!.Value - remaining.PlannedArrivalAt!.Value).TotalMinutes;
        Assert.Equal(expectedDelay, option.EtaDelayMinutes); Assert.True(option.EtaDelayMinutes < 0);
        Assert.Equal(new[] { "REQUEST_BACKUP", "ESCALATE_REROUTE", "CREATE_RETURN" }, rc.AvailableActions);
        var lm = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId);
        Assert.Equal(new[] { "APPROVE_BACKUP", "HOLD_ROUTE", "CREATE_RETURN" }, lm.AvailableActions);
        Assert.Equal(main.ComplianceDossierId, Assert.Single(lm.ComplianceDossiers).ComplianceDossierId);
        Assert.Equal(version, item.UpdatedAt); Assert.Equal(IncidentStatus.REPORTED, item.Status);
        Assert.Equal(RoutePlanStatus.CANCELLED, cancelled.Status); Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);

        remaining.SupersededAt = Scenario.Now; backup.Status = RoutePlanStatus.ACTIVE; await db.SaveChangesAsync();
        var switched = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId);
        Assert.Equal(backup.RoutePlanRoutes.Single(segment => segment.RouteType == RouteType.BACKUP).RoutePlanRouteId,
            Assert.Single(switched.RemainingSegments).RoutePlanRouteId);
        Assert.Equal(f.Segment.RoutePlanRouteId, switched.CurrentSegment!.RoutePlanRouteId);
        Assert.Equal("ALREADY_ACTIVE", Assert.Single(switched.BackupPlans).UnavailableReason);
        Assert.DoesNotContain("APPROVE_BACKUP", switched.AvailableActions);
    }

    [Fact]
    public async Task Context_GatesReturnActivationOnBothDossiers_AndClosedIncidentsHaveNoActions()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        db.Add(PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2));
        var returning = AddPlan(db, f, RouteType.RETURN); var main = AddDossier(db, f, DossierType.MAIN);
        var extra = AddDossier(db, f, DossierType.EXTRA, ComplianceDossierStatus.DRAFT); await db.SaveChangesAsync();
        var context = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId);
        Assert.Equal(returning.RoutePlanId, context.ReturnPlan!.RoutePlanId);
        Assert.DoesNotContain("ACTIVATE_RETURN", context.AvailableActions); Assert.DoesNotContain("CREATE_RETURN", context.AvailableActions);
        extra.Status = ComplianceDossierStatus.APPROVED; await db.SaveChangesAsync();
        Assert.Contains("ACTIVATE_RETURN", (await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId)).AvailableActions);
        main.Status = ComplianceDossierStatus.DRAFT; await db.SaveChangesAsync();
        Assert.DoesNotContain("ACTIVATE_RETURN", (await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId)).AvailableActions);
        returning.Status = RoutePlanStatus.REJECTED_BY_MANAGER; await db.SaveChangesAsync();
        context = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId);
        Assert.Null(context.ReturnPlan); Assert.Contains("CREATE_RETURN", context.AvailableActions);
        item.Status = IncidentStatus.RESOLVED; await db.SaveChangesAsync();
        Assert.Empty((await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId)).AvailableActions);
        f.Transport.Status = TransportStatus.COMPLETED; f.Segment.ActualArrivalAt = Scenario.Now; await db.SaveChangesAsync();
        Assert.Null((await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId)).CurrentSegment);
        Assert.Empty(db.Notifications);
    }

    [Theory]
    [InlineData("draft", "NOT_CONFIRMED")]
    [InlineData("departed", "SEGMENT_LOCKED")]
    [InlineData("no-remaining", "SEGMENT_LOCKED")]
    [InlineData("long-drive", "REST_RULE_VIOLATION")]
    [InlineData("missing-schedule", "REST_RULE_VIOLATION")]
    public async Task Context_ExplainsUnavailableBackups(string condition, string reason)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        if (condition != "no-remaining") db.Add(PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2));
        var backup = AddPlan(db, f, RouteType.BACKUP); var segment = backup.RoutePlanRoutes.Single();
        if (condition == "draft") backup.Status = RoutePlanStatus.DRAFT;
        if (condition == "departed") segment.ActualDepartureAt = Scenario.Now;
        if (condition == "long-drive") segment.PlannedArrivalAt = segment.PlannedDepartureAt!.Value.AddHours(9);
        if (condition == "missing-schedule") segment.PlannedArrivalAt = null;
        await db.SaveChangesAsync();
        var context = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId);
        var option = Assert.Single(context.BackupPlans); Assert.False(option.IsAvailable); Assert.Equal(reason, option.UnavailableReason);
        Assert.DoesNotContain("REQUEST_BACKUP", context.AvailableActions);
    }

    [Fact]
    public async Task Escalation_AcceptsIncident_OnlyAlertsActiveManagersAndAssignedSpecialists_WithDurableEmail()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var extraManager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var inactive = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER); inactive.IsActive = false;
        await EmployeeAsync(db, EmployeeRole.TRANSPORT_SPECIALIST); await db.SaveChangesAsync();
        var result = await f.Service.EscalateRerouteAsync(item.IncidentReportId, new()
        { Reason = "  Both roads are closed by snow.  ", SuggestedAction = "HOLD", SuggestedHoldMinutes = 90, BlockedRoutePlanIds = [f.Segment.RoutePlanId] }, f.Coordinator.AccountId);
        Assert.Equal("PENDING", result.IncidentStatus); Assert.True(item.UpdatedAt > Scenario.Now);
        Assert.Equal(new[] { f.Manager.EmployeeId, extraManager.EmployeeId }.Order(), result.NotifiedLogisticsManagers.Select(employee => employee.EmployeeId).Order());
        var alerts = await db.AccountNotifications.Include(row => row.Notification).ToArrayAsync();
        Assert.Equal(new[] { f.Manager.AccountId, extraManager.AccountId, f.Specialist.AccountId }.Order(), alerts.Select(row => row.AccountId).Order());
        Assert.All(alerts, row =>
        {
            Assert.Equal(NotificationType.ROUTE, row.Notification.NotificationType); Assert.Equal(NotificationPriority.URGENT, row.Notification.Priority);
            Assert.Equal("IncidentReport", row.Notification.ReferenceType); Assert.Equal($"/admin/incidents/{item.IncidentReportId}/reroute", row.Notification.ActionUrl);
            Assert.Contains("Both roads are closed by snow.", row.Notification.Message); Assert.Contains("HOLD for 90 minutes", row.Notification.Message);
            Assert.Contains(f.Segment.RoutePlanId.ToString(), row.Notification.Message);
        });
        Assert.Equal(2, await db.OutboxMessages.CountAsync(message => message.MessageType == "EMAIL"));
        Assert.Equal(TransportStatus.IN_TRANSIT, f.Transport.Status); Assert.Equal(RoutePlanStatus.ACTIVE, f.Segment.RoutePlan.Status);
        Assert.DoesNotContain(alerts, row => row.AccountId == f.CustomerAccountId);
    }

    [Fact]
    public async Task Hold_PersistsPhoneFreeDecision_AlertsCurrentCrewAndStaff_WithoutChangingExecutionState()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f);
        var nextDriver = await EmployeeAsync(db, EmployeeRole.DRIVER); var nextEscort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var nextSegment = PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2);
        nextSegment.DriverEmployeeId = nextDriver.EmployeeId; nextSegment.EscortEmployeeId = nextEscort.EmployeeId; db.Add(nextSegment);
        var version = f.Transport.UpdatedAt; var segmentVersion = f.Segment.UpdatedAt; await db.SaveChangesAsync();
        var result = await f.Service.HoldRouteAsync(item.IncidentReportId, new()
        { HoldMinutes = 60, Reason = "  Wait for both roads to reopen.  ", SafeStopLocationId = f.Location.LocationId }, f.Manager.AccountId);
        Assert.Equal("PENDING", result.IncidentStatus); Assert.Equal("ACTIVE", result.Hold.Status);
        Assert.Equal(DbTime.AsUtc(Scenario.Now), result.Hold.StartedAt); Assert.Equal(result.Hold.StartedAt.AddHours(1), result.Hold.HoldUntil);
        Assert.Equal(30, result.Hold.MonitorIntervalMinutes); Assert.Equal("Wait for both roads to reopen.", result.Hold.Reason);
        Assert.Equal(f.Location.LocationId, result.Hold.SafeStopLocation!.LocationId); Assert.Equal(f.Manager.EmployeeId, result.Hold.DecidedBy.EmployeeId);
        var canonical = Assert.Single(await db.Notifications.Where(row => row.ExpiresAt != null).ToArrayAsync());
        Assert.Equal("IncidentHold", canonical.ReferenceType); Assert.Equal(item.IncidentReportId, canonical.ReferenceId);
        Assert.Equal(Scenario.Now.AddHours(1), canonical.ExpiresAt); Assert.Empty(canonical.AccountNotifications);
        var alerts = await db.AccountNotifications.Include(row => row.Notification).ToArrayAsync();
        Assert.Equal(new[] { f.Driver.AccountId, f.Escort.AccountId, f.Coordinator.AccountId, f.Specialist.AccountId }.Order(), alerts.Select(row => row.AccountId).Order());
        Assert.All(alerts, row =>
        {
            var crew = row.AccountId == f.Driver.AccountId || row.AccountId == f.Escort.AccountId;
            Assert.Equal(crew ? NotificationPriority.URGENT : NotificationPriority.HIGH, row.Notification.Priority);
            Assert.Equal(NotificationType.ROUTE, row.Notification.NotificationType); Assert.Equal("IncidentHold", row.Notification.ReferenceType);
            Assert.Equal(crew ? $"/crew/incidents/{item.IncidentReportId}" : $"/admin/incidents/{item.IncidentReportId}/reroute", row.Notification.ActionUrl);
            Assert.Contains("every 30 minutes", row.Notification.Message); Assert.False(row.Notification.Message.StartsWith('{'));
        });
        Assert.Empty(db.OutboxMessages); Assert.Equal(TransportStatus.IN_TRANSIT, f.Transport.Status); Assert.Equal(version, f.Transport.UpdatedAt);
        Assert.Equal(RoutePlanStatus.ACTIVE, f.Segment.RoutePlan.Status); Assert.Equal(segmentVersion, f.Segment.UpdatedAt); Assert.Equal(VehicleStatus.IN_USE, f.Vehicle.Status);
        var detail = Assert.IsType<Incidents.Response.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.Manager.AccountId));
        Assert.Equal(result.Hold, detail.Hold); Assert.DoesNotContain("HOLD_ROUTE", detail.AvailableActions);
        Assert.Equal(result.Hold, Assert.IsType<Incidents.CustomerResponse.Incident>(await f.Service.GetAsync(item.IncidentReportId, f.CustomerAccountId)).Hold);
        var context = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId);
        Assert.Equal(result.Hold, context.Hold); Assert.DoesNotContain("HOLD_ROUTE", context.AvailableActions);
        Assert.DoesNotContain("phone", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        var other = AddIncident(db, f); await db.SaveChangesAsync();
        Assert.Equal("HOLD_ALREADY_ACTIVE", (await Assert.ThrowsAsync<ConflictException>(() => WriteAsync(f.Service, other.IncidentReportId, "hold", f.Manager.AccountId))).ErrorCode);
        var otherContext = await f.Service.GetRerouteContextAsync(other.IncidentReportId, f.Manager.AccountId);
        Assert.Null(otherContext.Hold); Assert.DoesNotContain("HOLD_ROUTE", otherContext.AvailableActions);
        Assert.Equal(IncidentStatus.REPORTED, other.Status); Assert.Equal(5, await db.Notifications.CountAsync());
        item.Status = IncidentStatus.RESOLVED; await db.SaveChangesAsync();
        Assert.Equal("HOLD_ALREADY_ACTIVE", (await Assert.ThrowsAsync<ConflictException>(() => WriteAsync(f.Service, other.IncidentReportId, "hold", f.Manager.AccountId))).ErrorCode);
    }

    [Fact]
    public async Task Expiry_IsDerivedFromDeadline_QueuesOnceAcrossServiceRestarts_WithoutAutomaticRouteChanges()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var clock = new MutableClock(); var service = CreateService(db, f.Storage, clock);
        await service.HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId);
        Assert.Equal(0, await service.QueueExpiredHoldsAsync()); var previousIds = await db.Notifications.Select(row => row.NotificationId).ToArrayAsync();
        clock.Now = clock.Now.AddMinutes(15); var version = item.UpdatedAt;
        Assert.Equal(1, await CreateService(db, f.Storage, clock).QueueExpiredHoldsAsync());
        Assert.Equal(0, await CreateService(db, f.Storage, clock).QueueExpiredHoldsAsync());
        var alerts = await db.AccountNotifications.Include(row => row.Notification).Where(row => !previousIds.Contains(row.NotificationId)).ToArrayAsync();
        Assert.Equal(new[] { f.Manager.AccountId, f.Coordinator.AccountId, f.Specialist.AccountId }.Order(), alerts.Select(row => row.AccountId).Order());
        Assert.All(alerts, row => { Assert.Equal(NotificationPriority.URGENT, row.Notification.Priority); Assert.Equal(NotificationType.ROUTE, row.Notification.NotificationType);
            Assert.Equal("IncidentHold", row.Notification.ReferenceType); Assert.Equal($"/admin/incidents/{item.IncidentReportId}/reroute", row.Notification.ActionUrl); });
        Assert.Single(await db.Notifications.Where(row => row.ReferenceType == "IncidentHoldExpired").ToArrayAsync());
        Assert.Single(await db.OutboxMessages.Where(message => message.MessageType == "EMAIL").ToArrayAsync());
        Assert.Equal("EXPIRED", (await CreateService(db, f.Storage, clock).GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId)).Hold!.Status);
        Assert.Equal(version, item.UpdatedAt); Assert.Equal(IncidentStatus.PENDING, item.Status); Assert.Equal(TransportStatus.IN_TRANSIT, f.Transport.Status);
        Assert.Equal(RoutePlanStatus.ACTIVE, f.Segment.RoutePlan.Status); Assert.Equal(VehicleStatus.IN_USE, f.Vehicle.Status);
    }

    [Theory]
    [InlineData("resolved")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    [InlineData("obsolete")]
    public async Task Expiry_SkipsClosedCompletedAndSupersededHolds(string condition)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var clock = new MutableClock(); var service = CreateService(db, f.Storage, clock);
        await service.HoldRouteAsync(item.IncidentReportId, HoldRequest(), f.Manager.AccountId); clock.Now = clock.Now.AddMinutes(16);
        if (condition == "resolved") item.Status = IncidentStatus.RESOLVED;
        if (condition == "cancelled") item.Status = IncidentStatus.CANCELLED;
        if (condition == "completed") f.Transport.Status = TransportStatus.COMPLETED;
        await db.SaveChangesAsync();
        if (condition == "obsolete")
        {
            var next = AddIncident(db, f); await db.SaveChangesAsync();
            await service.HoldRouteAsync(next.IncidentReportId, HoldRequest(), f.Manager.AccountId);
        }
        var count = await db.Notifications.CountAsync(); Assert.Equal(0, await service.QueueExpiredHoldsAsync());
        Assert.Equal(count, await db.Notifications.CountAsync()); Assert.Empty(db.OutboxMessages);
        Assert.Equal("EXPIRED", Assert.IsType<Incidents.Response.Incident>(await service.GetAsync(item.IncidentReportId, f.Manager.AccountId)).Hold!.Status);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("escalation")]
    public async Task Writes_EnforceRolesOwnershipIncidentAndTransportState_BeforeEffects(string action)
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var unrelated = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var denied = new[] { f.Specialist.AccountId, f.Driver.AccountId, f.Escort.AccountId, unrelated.AccountId,
            action == "hold" ? f.Coordinator.AccountId : f.Manager.AccountId };
        foreach (var accountId in denied)
            Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<ForbiddenException>(() => WriteAsync(f.Service, item.IncidentReportId, action, accountId))).ErrorCode);
        var actor = action == "hold" ? f.Manager.AccountId : f.Coordinator.AccountId;
        foreach (var status in new[] { IncidentStatus.RESOLVED, IncidentStatus.CANCELLED })
        {
            item.Status = status; await db.SaveChangesAsync();
            Assert.Equal("INVALID_INCIDENT_STATUS", (await Assert.ThrowsAsync<UnprocessableEntityException>(() => WriteAsync(f.Service, item.IncidentReportId, action, actor))).ErrorCode);
        }
        item.Status = IncidentStatus.REPORTED;
        foreach (var status in new[] { TransportStatus.READY, TransportStatus.COMPLETED, TransportStatus.CANCELLED })
        {
            f.Transport.Status = status; await db.SaveChangesAsync();
            Assert.Equal("TRANSPORT_NOT_IN_TRANSIT", (await Assert.ThrowsAsync<ConflictException>(() => WriteAsync(f.Service, item.IncidentReportId, action, actor))).ErrorCode);
        }
        Assert.Equal("INCIDENT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => WriteAsync(f.Service, Guid.NewGuid(), action, actor))).ErrorCode);
        Assert.Empty(db.Notifications); Assert.Empty(db.OutboxMessages);
    }

    [Fact]
    public async Task Context_RejectsUnassignedCoordinatorAndOtherRoles_UsesLatestNonCancelledTransportPlan()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var unrelated = await EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        foreach (var accountId in new[] { unrelated.AccountId, f.Specialist.AccountId, f.Driver.AccountId, f.Escort.AccountId, f.CustomerAccountId })
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetRerouteContextAsync(item.IncidentReportId, accountId));
        var oldBackup = AddPlan(db, f, RouteType.BACKUP);
        var latest = new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-new", TransportId = f.Transport.TransportId,
            Status = TransportPlanStatus.APPROVED, CreatedAt = Scenario.Now.AddHours(1), UpdatedAt = Scenario.Now };
        db.Add(latest); db.Add(new TransportPlan { TransportPlanId = Guid.NewGuid(), PlanCode = "TP-cancelled", TransportId = f.Transport.TransportId,
            Status = TransportPlanStatus.CANCELLED, CreatedAt = Scenario.Now.AddHours(2), UpdatedAt = Scenario.Now }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Coordinator.AccountId));
        var context = await f.Service.GetRerouteContextAsync(item.IncidentReportId, f.Manager.AccountId);
        Assert.Equal(latest.TransportPlanId, context.Transport.TransportPlanId); Assert.Empty(context.BackupPlans); Assert.Null(context.CurrentSegment);
        Assert.Equal(RoutePlanStatus.CONFIRMED, oldBackup.Status); Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task Writes_RejectForeignPlansAndMissingOrDeletedStops_WithoutAcceptingIncident()
    {
        await using var db = TestSupport.CreateDb(); var f = await Scenario.CreateAsync(db); var item = AddIncident(db, f); await db.SaveChangesAsync();
        var other = await Scenario.CreateAsync(db);
        foreach (var id in new[] { Guid.NewGuid(), other.Segment.RoutePlanId })
            await Assert.ThrowsAsync<BadRequestException>(() => f.Service.EscalateRerouteAsync(item.IncidentReportId,
                new() { Reason = Reason, BlockedRoutePlanIds = [id] }, f.Coordinator.AccountId));
        foreach (var id in new[] { Guid.NewGuid(), f.Location.LocationId })
        {
            f.Location.IsDeleted = true; await db.SaveChangesAsync();
            Assert.Equal("LOCATION_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.HoldRouteAsync(item.IncidentReportId,
                new() { HoldMinutes = 15, Reason = Reason, SafeStopLocationId = id }, f.Manager.AccountId))).ErrorCode);
        }
        Assert.Equal(IncidentStatus.REPORTED, item.Status); Assert.Empty(db.Notifications);
    }

    [Fact]
    public void Validators_RejectBoundaryAndMalformedInputs()
    {
        var hold = new RouteHoldRequestValidator(); var escalation = new RerouteEscalationRequestValidator();
        Assert.True(hold.Validate(HoldRequest()).IsValid);
        foreach (var minutes in new[] { 0, 14, 241 }) Assert.False(hold.Validate(new Incidents.Request.RouteHoldRequest { HoldMinutes = minutes, Reason = Reason }).IsValid);
        foreach (var minutes in new[] { 9, 121 }) Assert.False(hold.Validate(new Incidents.Request.RouteHoldRequest { HoldMinutes = 15, Reason = Reason, MonitorIntervalMinutes = minutes }).IsValid);
        foreach (var reason in new[] { " ", " 123456789 ", new string('x', 2001) })
        {
            Assert.False(hold.Validate(new Incidents.Request.RouteHoldRequest { HoldMinutes = 15, Reason = reason }).IsValid);
            Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = reason }).IsValid);
        }
        Assert.False(hold.Validate(new Incidents.Request.RouteHoldRequest { HoldMinutes = 15, Reason = Reason, SafeStopLocationId = Guid.Empty }).IsValid);
        Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, SuggestedAction = "BACKUP" }).IsValid);
        Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, SuggestedAction = "RETURN", SuggestedHoldMinutes = 15 }).IsValid);
        Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, SuggestedAction = "HOLD", SuggestedHoldMinutes = 241 }).IsValid);
        Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, BlockedRoutePlanIds = [Guid.Empty] }).IsValid);
        var duplicate = Guid.NewGuid(); Assert.False(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, BlockedRoutePlanIds = [duplicate, duplicate] }).IsValid);
        Assert.True(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, SuggestedAction = "HOLD", SuggestedHoldMinutes = 15 }).IsValid);
        Assert.True(escalation.Validate(new Incidents.Request.RerouteEscalationRequest { Reason = Reason, SuggestedAction = "RETURN" }).IsValid);
    }

    internal static Incidents.Request.RouteHoldRequest HoldRequest() => new() { HoldMinutes = 15, Reason = Reason };
    internal static Task WriteAsync(Incidents.Service service, Guid incidentId, string action, Guid accountId) => action == "hold"
        ? service.HoldRouteAsync(incidentId, HoldRequest(), accountId)
        : service.EscalateRerouteAsync(incidentId, new() { Reason = Reason }, accountId);

    internal static RoutePlan AddPlan(AppDbContext db, Scenario f, RouteType type, RoutePlanStatus status = RoutePlanStatus.CONFIRMED)
    {
        var plan = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-" + Guid.NewGuid().ToString("N"),
            TransportPlanId = f.Segment.RoutePlan.TransportPlanId, PrimaryRoutePlanId = type == RouteType.BACKUP ? f.Segment.RoutePlanId : null,
            Status = status, TotalDistanceKm = 100, EstimatedDurationMinutes = 60, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now };
        var segment = PersonnelAssignmentTests.Segment(plan, type == RouteType.BACKUP ? 2 : 1); segment.RouteType = type;
        segment.PlannedDepartureAt = Scenario.Now.AddHours(2); segment.PlannedArrivalAt = Scenario.Now.AddHours(3); db.Add(segment);
        return plan;
    }

    internal static ComplianceDossier AddDossier(AppDbContext db, Scenario f, DossierType type, ComplianceDossierStatus status = ComplianceDossierStatus.APPROVED)
    {
        var dossier = new ComplianceDossier { ComplianceDossierId = Guid.NewGuid(), DossierCode = "CD-" + Guid.NewGuid().ToString("N"),
            TransportId = f.Transport.TransportId, DossierType = type, Status = status, OpenedAt = Scenario.Now, CreatedAt = Scenario.Now, UpdatedAt = Scenario.Now };
        db.Add(dossier); return dossier;
    }

    internal sealed class MutableClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DbTime.AsUtc(Scenario.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
