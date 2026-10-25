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
    public async Task Change_KeepsTheOldVehicleFlagWhenAnotherConfirmedPlanHoldsIt()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        await f.AlternateAsync(RouteType.BACKUP);

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal(VehicleStatus.ASSIGNED, old.Status); Assert.Empty(result.ReleasedVehicleIds);
    }

    [Fact]
    public async Task Change_LeavesAnInUseOldVehicleInUse()
    {
        await using var db = TestSupport.CreateDb(); var (f, old, replacement) = await CreateAsync(db);
        old.Status = VehicleStatus.IN_USE; await db.SaveChangesAsync();

        var result = await TestSupport.CreateRoutePlans(db).ChangeVehiclesAsync(f.Coordinator.AccountId, f.Plan.RoutePlanId, Change(f, replacement.VehicleId));

        Assert.Equal(VehicleStatus.IN_USE, old.Status); Assert.Empty(result.ReleasedVehicleIds);
    }

    [Theory]
    [InlineData(RoutePlanStatus.DRAFT)]
    [InlineData(RoutePlanStatus.ACTIVE)]
    public async Task Change_IsOnlyAllowedWhileTheRoutePlanIsConfirmed(RoutePlanStatus status)
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
