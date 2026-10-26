using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using RoutePlanRequest = Pegaxus.Service.RoutePlanService.Request;

namespace Pegaxus.Service.Test;

public class RoutePlanMidTripVehicleChangeTests
{
    private sealed record Seed(RouteChangeTests.Fixture F, Vehicle Old, Vehicle Replacement, IncidentReport Incident, VehicleIncidentDetail Detail, EmergencyCostRequest Cost);

    private static async Task<Seed> CreateAsync(AppDbContext db)
    {
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var old = await db.Vehicles.SingleAsync(); old.Status = VehicleStatus.IN_USE;
        var replacement = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V2", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        var incident = new IncidentReport { IncidentReportId = Guid.NewGuid(), TransportId = f.Transport.TransportId, IncidentCode = "INC-1", EmployeeId = f.Driver.EmployeeId,
            LocationId = f.Start.LocationId, Category = IncidentCategory.VEHICLE, Severity = IncidentSeverity.HIGH, Status = IncidentStatus.REPORTED, Title = "Engine failure", Description = "Engine failed" };
        var detail = new VehicleIncidentDetail { VehicleIncidentDetailId = Guid.NewGuid(), IncidentReport = incident, VehicleId = old.VehicleId, VehicleOperable = false, RepairRequired = true, ReplacementVehicleRequired = true };
        var cost = new EmergencyCostRequest { EmergencyCostRequestId = Guid.NewGuid(), IncidentReport = incident, RequestCode = "ECR-1", Category = EmergencyCostCategory.REPLACEMENT_VEHICLE,
            Status = EmergencyCostStatus.APPROVED, RequestedByEmployeeId = f.Driver.EmployeeId, Description = "Hire a truck", Justification = "Engine failure", Currency = "VND", RequestedAmount = 30_000_000 };
        db.AddRange(replacement, incident, detail, cost);
        await db.SaveChangesAsync();
        return new(f, old, replacement, incident, detail, cost);
    }

    private static RoutePlanRequest.ChangeVehiclesRequest Change(Seed seed, params Guid[] vehicleIds) => new()
    {
        LastUpdatedAt = RouteChangeTests.Fixture.Version(seed.F.Plan), Reason = "Truck broke down", IncidentReportId = seed.Incident.IncidentReportId,
        Segments = [new() { RoutePlanRouteId = seed.F.Current.RoutePlanRouteId, VehicleIds = [.. vehicleIds] }]
    };

    private static Task<Pegaxus.Service.RoutePlanService.Response.VehicleChangeResponse> Run(AppDbContext db, Seed seed, RoutePlanRequest.ChangeVehiclesRequest request) =>
        TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(seed.F.Coordinator.AccountId, seed.F.Plan.RoutePlanId, request);

    [Fact]
    public async Task Change_ReplacesTheBrokenVehicleOnTheSegmentOnTheRoad()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.F.Current.ActualDepartureAt = seed.F.Current.PlannedDepartureAt; await db.SaveChangesAsync();

        var result = await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync()).VehicleId);
        Assert.Equal(VehicleStatus.IN_USE, seed.Replacement.Status); Assert.Equal(VehicleStatus.MAINTENANCE, seed.Old.Status);
        Assert.Equal([seed.Old.VehicleId], result.MaintenanceVehicleIds); Assert.Empty(result.ReleasedVehicleIds); Assert.Equal("ACTIVE", result.Status);
        var audit = await db.CatalogAuditLogs.SingleAsync(); Assert.Equal("ROUTE_PLAN_VEHICLES_CHANGED", audit.ActionType); Assert.Contains(seed.Incident.IncidentReportId.ToString(), audit.AfterData!.RootElement.GetRawText());
    }

    [Fact]
    public async Task Change_ReturnsAnOperableReplacedVehicleToThePool()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Detail.VehicleOperable = true; seed.Detail.RepairRequired = false; await db.SaveChangesAsync();

        var result = await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal(VehicleStatus.AVAILABLE, seed.Old.Status); Assert.Equal([seed.Old.VehicleId], result.ReleasedVehicleIds); Assert.Empty(result.MaintenanceVehicleIds);
    }

    [Fact]
    public async Task Change_OnASegmentThatHasNotLeftYet_OnlyReservesTheNewVehicle()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);

        await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal(VehicleStatus.ASSIGNED, seed.Replacement.Status);
    }

    [Fact]
    public async Task Change_TellsTheCrewAndTheManagerUrgently()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);

        await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        var notifications = await db.AccountNotifications.Include(item => item.Notification).ToListAsync();
        Assert.Contains(notifications, item => item.AccountId == seed.F.Driver.AccountId && item.Notification.Priority == NotificationPriority.URGENT);
        Assert.Contains(notifications, item => item.AccountId == seed.F.Manager.AccountId);
        Assert.DoesNotContain(notifications, item => item.AccountId == seed.F.Coordinator.AccountId);
    }

    [Fact]
    public async Task Change_ChangesTheConfirmedBackupSegmentInTheSameCall_AndReleasesTheOldVehicle()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        var request = Change(seed, seed.Replacement.VehicleId);
        request.Segments.Add(new() { RoutePlanRouteId = backupSegment.RoutePlanRouteId, VehicleIds = [seed.Replacement.VehicleId] });

        var result = await Run(db, seed, request);

        Assert.All(await db.RoutePlanRouteVehicles.ToListAsync(), item => Assert.Equal(seed.Replacement.VehicleId, item.VehicleId));
        Assert.Equal(2, await db.RoutePlanRouteVehicles.CountAsync()); Assert.Equal([seed.Old.VehicleId], result.MaintenanceVehicleIds);
    }

    [Fact]
    public async Task Change_AppliesToTheConfirmedBackup_SkippingSegmentsBeforeTheBranch()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var copy = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.PRIMARY);
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = copy.RoutePlanRouteId, VehicleId = seed.Old.VehicleId }); await db.SaveChangesAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        var request = Change(seed, seed.Replacement.VehicleId);

        var result = await Run(db, seed, request);

        Assert.Equal([backupSegment.RoutePlanRouteId], result.AppliedSegmentIds);
        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == backupSegment.RoutePlanRouteId)).VehicleId);
        Assert.Equal(seed.Old.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == copy.RoutePlanRouteId)).VehicleId);
        Assert.Equal(VehicleStatus.MAINTENANCE, seed.Old.Status);
    }

    [Fact]
    public async Task Change_AppliedSegmentsGoThroughTheSameChecks_AndNothingIsSavedWhenOneFails()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        var later = seed.F.Current.PlannedDepartureAt!.Value.AddHours(10);
        backupSegment.PlannedDepartureAt = later; backupSegment.PlannedArrivalAt = later.AddHours(2);
        var otherPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR-OTHER", BookingId = Guid.NewGuid() },
            PlanCode = "TP-OTHER", Status = TransportPlanStatus.APPROVED };
        var busy = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlan = otherPlan, RoutePlanCode = "RP-BUSY", Status = RoutePlanStatus.CONFIRMED },
            RouteId = seed.F.Current.RouteId, PlannedDepartureAt = later, PlannedArrivalAt = later.AddHours(2) };
        db.Add(busy); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = busy, VehicleId = seed.Replacement.VehicleId }); await db.SaveChangesAsync();
        var request = Change(seed, seed.Replacement.VehicleId);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Run(db, seed, request));

        Assert.Equal("VEHICLE_SCHEDULE_CONFLICT", error.ErrorCode);
        Assert.Equal(seed.Old.VehicleId, (await db.RoutePlanRouteVehicles.AsNoTracking().SingleAsync(item => item.RoutePlanRouteId == seed.F.Current.RoutePlanRouteId)).VehicleId);
    }

    [Fact]
    public async Task Change_KeepsTheBrokenVehicleFlagWhileAnotherTransportStillHoldsIt()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var otherPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR-OTHER", BookingId = Guid.NewGuid() },
            PlanCode = "TP-OTHER", Status = TransportPlanStatus.APPROVED };
        var held = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlan = otherPlan, RoutePlanCode = "RP-OTHER", Status = RoutePlanStatus.CONFIRMED },
            RouteId = seed.F.Current.RouteId, PlannedDepartureAt = seed.F.Current.PlannedDepartureAt!.Value.AddDays(5), PlannedArrivalAt = seed.F.Current.PlannedArrivalAt!.Value.AddDays(5) };
        db.Add(held); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = held, VehicleId = seed.Old.VehicleId }); await db.SaveChangesAsync();

        var result = await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal(VehicleStatus.IN_USE, seed.Old.Status); Assert.Empty(result.MaintenanceVehicleIds); Assert.Empty(result.ReleasedVehicleIds);
    }

    [Fact]
    public async Task Change_CarriesTheReplacementOverToTheConfirmedBackup_AndLeavesAListedOneAlone()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);

        var result = await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal([backupSegment.RoutePlanRouteId], result.AppliedSegmentIds);
        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == backupSegment.RoutePlanRouteId)).VehicleId);
        Assert.Equal([seed.Old.VehicleId], result.MaintenanceVehicleIds);
    }

    [Fact]
    public async Task Change_WhileRunningOnAnActiveBackup_CarriesTheReplacementToItsLaterSegments()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync(RouteType.BACKUP, RoutePlanStatus.ACTIVE);
        var running = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        running.ActualDepartureAt = running.PlannedDepartureAt;
        seed.F.Current.SupersededAt = running.PlannedDepartureAt;
        var other = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V3", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.ASSIGNED,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        RoutePlanRoute Later(int sequenceNo) => new() { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = backup.RoutePlanId, RouteId = seed.F.Current.RouteId, SequenceNo = sequenceNo,
            RouteType = RouteType.BACKUP, PlannedDepartureAt = running.PlannedArrivalAt!.Value.AddHours((sequenceNo - 3) * 2), PlannedArrivalAt = running.PlannedArrivalAt!.Value.AddHours((sequenceNo - 2) * 2) };
        var third = Later(3); var fourth = Later(4);
        db.AddRange(other, third, fourth);
        db.RoutePlanRouteVehicles.AddRange(new RoutePlanRouteVehicle { RoutePlanRoute = third, VehicleId = seed.Old.VehicleId }, new RoutePlanRouteVehicle { RoutePlanRoute = fourth, VehicleId = other.VehicleId });
        await db.SaveChangesAsync();
        var request = Change(seed, seed.Replacement.VehicleId);
        request.LastUpdatedAt = RouteChangeTests.Fixture.Version(backup); request.Segments[0].RoutePlanRouteId = running.RoutePlanRouteId;

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(seed.F.Coordinator.AccountId, backup.RoutePlanId, request);

        Assert.Equal([third.RoutePlanRouteId], result.AppliedSegmentIds);
        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == running.RoutePlanRouteId)).VehicleId);
        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == third.RoutePlanRouteId)).VehicleId);
        Assert.Equal(other.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == fourth.RoutePlanRouteId)).VehicleId);
        Assert.Equal(seed.Old.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == seed.F.Current.RoutePlanRouteId)).VehicleId);
        Assert.Equal(VehicleStatus.IN_USE, seed.Replacement.Status); Assert.Equal(VehicleStatus.MAINTENANCE, seed.Old.Status);
    }

    [Fact]
    public async Task Change_RefusesAVehicleThatIsInUseOnAnotherTripForAnySegment()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Replacement.Status = VehicleStatus.IN_USE; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("VEHICLE_UNAVAILABLE", error.ErrorCode);
    }

    [Fact]
    public async Task Change_AllowsAVehicleAssignedToALaterTripWhenTheTimesDoNotOverlap()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Replacement.Status = VehicleStatus.ASSIGNED; await db.SaveChangesAsync();

        await Run(db, seed, Change(seed, seed.Replacement.VehicleId));

        Assert.Equal(seed.Replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync()).VehicleId);
    }

    [Fact]
    public async Task Change_IgnoresBackupSegmentsBeforeTheBranchWhenReleasing()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var copy = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.PRIMARY);
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = copy.RoutePlanRouteId, VehicleId = seed.Old.VehicleId }); await db.SaveChangesAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        var request = Change(seed, seed.Replacement.VehicleId);
        request.Segments.Add(new() { RoutePlanRouteId = backupSegment.RoutePlanRouteId, VehicleIds = [seed.Replacement.VehicleId] });

        await Run(db, seed, request);

        Assert.Equal(VehicleStatus.MAINTENANCE, seed.Old.Status);
    }

    [Fact]
    public async Task Change_RefusesASegmentBeforeTheBranchOfABackupPlan()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var backup = await seed.F.AlternateAsync();
        var copy = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.PRIMARY);
        var request = Change(seed, seed.Replacement.VehicleId); request.Segments[0].RoutePlanRouteId = copy.RoutePlanRouteId;

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, request));

        Assert.Equal("SEGMENT_NOT_IN_ITINERARY", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RefusesACompletedSegment()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var request = Change(seed, seed.Replacement.VehicleId); request.Segments[0].RoutePlanRouteId = seed.F.Completed.RoutePlanRouteId;

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, request));

        Assert.Equal("SEGMENT_ALREADY_COMPLETED", error.ErrorCode);
    }

    [Fact]
    public async Task Change_NeedsTheIncident()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var request = Change(seed, seed.Replacement.VehicleId); request.IncidentReportId = null;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Run(db, seed, request));

        Assert.Equal("INCIDENT_REQUIRED", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RefusesAnIncidentOfAnotherTransport()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Incident.TransportId = Guid.NewGuid(); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<NotFoundException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("INCIDENT_NOT_FOUND", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RefusesAResolvedIncident()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Incident.Status = IncidentStatus.RESOLVED; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("INVALID_INCIDENT_STATUS", error.ErrorCode);
    }

    [Fact]
    public async Task Change_OnlyReplacesAVehicleTheIncidentReports()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var other = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V3", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE };
        db.Add(other); seed.Detail.VehicleId = other.VehicleId; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("VEHICLE_NOT_IN_INCIDENT", error.ErrorCode);
    }

    [Theory]
    [InlineData(EmergencyCostStatus.PENDING)]
    [InlineData(EmergencyCostStatus.REJECTED)]
    [InlineData(EmergencyCostStatus.CANCELLED)]
    public async Task Change_NeedsAnApprovedReplacementCost(EmergencyCostStatus status)
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Cost.Status = status; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("REPLACEMENT_COST_NOT_APPROVED", error.ErrorCode);
    }

    [Fact]
    public async Task Change_AnApprovedCostOfAnotherCategoryDoesNotCount()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.Cost.Category = EmergencyCostCategory.TOWING; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("REPLACEMENT_COST_NOT_APPROVED", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RefusesANewVehicleThatIsOutOnAnotherTripWhenTheSegmentIsOnTheRoad()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.F.Current.ActualDepartureAt = seed.F.Current.PlannedDepartureAt; seed.Replacement.Status = VehicleStatus.IN_USE; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("VEHICLE_UNAVAILABLE", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RefusesSequenceNoWhileActive()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        var request = Change(seed, seed.Replacement.VehicleId); request.Segments[0].RoutePlanRouteId = null; request.Segments[0].SequenceNo = seed.F.Current.SequenceNo;

        await Assert.ThrowsAsync<BadRequestException>(() => Run(db, seed, request));
    }

    [Fact]
    public async Task Change_RefusesATransportThatIsNotOnTheRoad()
    {
        await using var db = TestSupport.CreateDb(); var seed = await CreateAsync(db);
        seed.F.Transport.Status = TransportStatus.READY; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => Run(db, seed, Change(seed, seed.Replacement.VehicleId)));

        Assert.Equal("TRANSPORT_NOT_IN_TRANSIT", error.ErrorCode);
    }
}
