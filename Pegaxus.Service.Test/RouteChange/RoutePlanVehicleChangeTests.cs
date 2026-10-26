using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using RoutePlanRequest = Pegaxus.Service.RoutePlanService.Request;

namespace Pegaxus.Service.Test;

public class RoutePlanVehicleChangeTests
{
    private static async Task<(RouteChangeTests.Fixture F, Vehicle Old, Vehicle Replacement)> CreateAsync(AppDbContext db, RoutePlanStatus status = RoutePlanStatus.CONFIRMED)
    {
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var old = await db.Vehicles.SingleAsync(); old.Status = VehicleStatus.ASSIGNED;
        var replacement = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V2", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        db.Add(replacement);
        f.Plan.Status = status; f.Transport.Status = TransportStatus.READY;
        await db.SaveChangesAsync();
        return (f, old, replacement);
    }

    private static RoutePlanRequest.ChangeVehiclesRequest Change(RouteChangeTests.Fixture f, params Guid[] vehicleIds) => new()
    {
        LastUpdatedAt = RouteChangeTests.Fixture.Version(f.Plan), Reason = "Previous trip is running late",
        Segments = [new() { SequenceNo = f.Current.SequenceNo, VehicleIds = [.. vehicleIds] }]
    };

    [Fact]
    public async Task Change_SwapsTheVehicle_AssignsTheNewOne_AndReleasesTheOldOne()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal(replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync()).VehicleId);
        Assert.Equal(VehicleStatus.ASSIGNED, replacement.Status); Assert.Equal(VehicleStatus.AVAILABLE, old.Status);
        Assert.Equal([old.VehicleId], result.ReleasedVehicleIds); Assert.Equal("CONFIRMED", result.Status);
        var audit = await db.CatalogAuditLogs.SingleAsync(); Assert.Equal("ROUTE_PLAN_VEHICLES_CHANGED", audit.ActionType); Assert.Equal("Previous trip is running late", audit.Summary);
    }

    [Fact]
    public async Task Change_KeepsTheOldVehicleFlagWhenAnotherTransportHoldsIt()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        var otherPlan = new TransportPlan { TransportPlanId = Guid.NewGuid(), Transport = new() { TransportId = Guid.NewGuid(), TransportCode = "TR-OTHER", BookingId = Guid.NewGuid() },
            PlanCode = "TP-OTHER", Status = TransportPlanStatus.APPROVED };
        var held = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlan = otherPlan, RoutePlanCode = "RP-OTHER", Status = RoutePlanStatus.CONFIRMED },
            RouteId = f.Current.RouteId, PlannedDepartureAt = f.Current.PlannedDepartureAt!.Value.AddDays(5), PlannedArrivalAt = f.Current.PlannedArrivalAt!.Value.AddDays(5) };
        db.Add(held); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = held, VehicleId = old.VehicleId }); await db.SaveChangesAsync();

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal(VehicleStatus.ASSIGNED, old.Status); Assert.Empty(result.ReleasedVehicleIds);
    }

    [Fact]
    public async Task Change_CarriesTheReplacementOverToTheConfirmedBackup()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        var backup = await f.AlternateAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal([backupSegment.RoutePlanRouteId], result.AppliedSegmentIds);
        Assert.All(await db.RoutePlanRouteVehicles.ToListAsync(), item => Assert.Equal(replacement.VehicleId, item.VehicleId));
        Assert.Equal(VehicleStatus.AVAILABLE, old.Status); Assert.Equal([old.VehicleId], result.ReleasedVehicleIds);
    }

    [Fact]
    public async Task Change_LeavesAListedBackupSegmentWithItsOwnVehicle()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var second = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V3", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        db.Add(second); await db.SaveChangesAsync();
        var backup = await f.AlternateAsync();
        var backupSegment = await db.RoutePlanRoutes.SingleAsync(item => item.RoutePlanId == backup.RoutePlanId && item.RouteType == RouteType.BACKUP);
        var request = Change(f, replacement.VehicleId); request.Segments.Add(new() { RoutePlanRouteId = backupSegment.RoutePlanRouteId, VehicleIds = [second.VehicleId] });

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request);

        Assert.Empty(result.AppliedSegmentIds);
        Assert.Equal(second.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync(item => item.RoutePlanRouteId == backupSegment.RoutePlanRouteId)).VehicleId);
    }

    private static async Task<(Vehicle Second, RoutePlanRoute Later)> AddLaterSegmentAsync(AppDbContext db, RouteChangeTests.Fixture f, Vehicle old)
    {
        var second = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V3", CapacityHorses = 5, TransportMode = TransportMode.ROAD, Status = VehicleStatus.AVAILABLE,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        var later = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = f.Plan.RoutePlanId, RouteId = f.Current.RouteId, SequenceNo = 3,
            PlannedDepartureAt = f.Current.PlannedArrivalAt, PlannedArrivalAt = f.Current.PlannedArrivalAt!.Value.AddHours(2) };
        db.AddRange(second, later); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = later, VehicleId = old.VehicleId }); await db.SaveChangesAsync();
        return (second, later);
    }

    [Fact]
    public async Task Change_RefusesWhenTheSameVehicleGetsDifferentReplacementsAndAnUnlistedSegmentStillUsesIt()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        var (second, _) = await AddLaterSegmentAsync(db, f, old);
        await f.AlternateAsync();
        var request = Change(f, replacement.VehicleId); request.Segments.Add(new() { SequenceNo = 3, VehicleIds = [second.VehicleId] });

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request));

        Assert.Equal("AMBIGUOUS_REPLACEMENT", error.ErrorCode);
    }

    [Fact]
    public async Task Change_AcceptsDifferentReplacementsWhenEverySegmentIsListed()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        var (second, _) = await AddLaterSegmentAsync(db, f, old);
        var backup = await f.AlternateAsync();
        var backupSegments = await db.RoutePlanRoutes.Where(item => item.RoutePlanId == backup.RoutePlanId).ToDictionaryAsync(item => item.SequenceNo);
        var request = Change(f, replacement.VehicleId);
        request.Segments.Add(new() { SequenceNo = 3, VehicleIds = [second.VehicleId] });
        request.Segments.Add(new() { RoutePlanRouteId = backupSegments[2].RoutePlanRouteId, VehicleIds = [replacement.VehicleId] });
        request.Segments.Add(new() { RoutePlanRouteId = backupSegments[3].RoutePlanRouteId, VehicleIds = [second.VehicleId] });

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request);

        Assert.Empty(result.AppliedSegmentIds);
        Assert.DoesNotContain(await db.RoutePlanRouteVehicles.ToListAsync(), item => item.VehicleId == old.VehicleId);
    }

    [Fact]
    public async Task Change_LeavesAnInUseOldVehicleInUse()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        old.Status = VehicleStatus.IN_USE; await db.SaveChangesAsync();

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal(VehicleStatus.IN_USE, old.Status); Assert.Empty(result.ReleasedVehicleIds);
    }

    [Fact]
    public async Task Change_BeforeDepartureDoesNotTakeAnIncident()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var request = Change(f, replacement.VehicleId); request.IncidentReportId = Guid.NewGuid();

        await Assert.ThrowsAsync<BadRequestException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request));
    }

    [Fact]
    public async Task Change_BeforeDepartureAcceptsRoutePlanRouteId()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var request = Change(f, replacement.VehicleId); request.Segments[0].SequenceNo = null; request.Segments[0].RoutePlanRouteId = f.Current.RoutePlanRouteId;

        await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request);

        Assert.Equal(replacement.VehicleId, (await db.RoutePlanRouteVehicles.SingleAsync()).VehicleId);
    }

    [Theory]
    [InlineData(RoutePlanStatus.DRAFT)]
    [InlineData(RoutePlanStatus.COMPLETED)]
    public async Task Change_IsOnlyAllowedWhileTheRoutePlanIsConfirmedOrActive(RoutePlanStatus status)
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db, status);

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId)));

        Assert.Equal("INVALID_ROUTE_PLAN_STATUS", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RejectsAStaleVersion()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var request = Change(f, replacement.VehicleId); request.LastUpdatedAt = request.LastUpdatedAt.AddMinutes(-1);

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request));

        Assert.Equal("ROUTE_PLAN_MODIFIED", error.ErrorCode);
    }

    [Fact]
    public async Task Change_OnlyTheRouteCoordinatorMayUseIt()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Manager.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId)));
    }

    [Theory]
    [InlineData(VehicleStatus.MAINTENANCE, "VEHICLE_UNAVAILABLE")]
    [InlineData(VehicleStatus.INACTIVE, "VEHICLE_UNAVAILABLE")]
    public async Task Change_AppliesThePlanningChecksToTheNewVehicle(VehicleStatus status, string code)
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        replacement.Status = status; await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<UnprocessableEntityException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId)));

        Assert.Equal(code, error.ErrorCode);
    }

    [Fact]
    public async Task Change_RejectsANewVehicleThatClashesWithAnotherTransportPlan()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var other = new TransportPlan { TransportPlanId = Guid.NewGuid(), TransportId = f.Transport.TransportId, PlanCode = "TP2", Status = TransportPlanStatus.APPROVED };
        var busy = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = new() { RoutePlanId = Guid.NewGuid(), TransportPlan = other, RoutePlanCode = "BUSY", Status = RoutePlanStatus.CONFIRMED },
            RouteId = f.Current.RouteId, PlannedDepartureAt = f.Current.PlannedDepartureAt, PlannedArrivalAt = f.Current.PlannedArrivalAt };
        db.Add(busy); db.RoutePlanRouteVehicles.Add(new() { RoutePlanRoute = busy, VehicleId = replacement.VehicleId }); await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId)));

        Assert.Equal("VEHICLE_SCHEDULE_CONFLICT", error.ErrorCode);
    }

    [Fact]
    public async Task Change_RejectsAnUnknownSegment()
    {
        await using var db = TestSupport.CreateDb(); var (f, _, replacement) = await CreateAsync(db);
        var request = Change(f, replacement.VehicleId); request.Segments[0].SequenceNo = 99;

        await Assert.ThrowsAsync<BadRequestException>(() => TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, request));
    }
}
