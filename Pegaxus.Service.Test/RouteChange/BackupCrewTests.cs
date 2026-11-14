using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.RouteChangeService.Validator;

using Request = Pegaxus.Service.RouteChangeService.Request;

namespace Pegaxus.Service.Test;

/// <summary>The manager chooses the backup crew, vehicles and start time when approving the change.</summary>
public class BackupCrewTests
{
    private static readonly DateTimeOffset Start = RouteChangeTests.Fixture.StartAt;

    [Fact]
    public async Task CrewPlan_SuggestsTheCrewOfTheUntravelledPrimarySegment()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db); var backup = await f.AlternateAsync();

        var plan = await f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId);

        var segment = Assert.Single(plan.Segments);
        Assert.Equal((f.Driver.EmployeeId, f.Escort.EmployeeId), (segment.Segment.Driver!.EmployeeId, segment.Segment.Escort!.EmployeeId));
        Assert.Equal(f.Current.RoutePlanRouteId, segment.SuggestedFromRoutePlanRouteId);
        Assert.False(segment.NeedsCrew);
        Assert.Equal(Start, plan.SuggestedStartAt);
    }

    [Fact]
    public async Task Approve_TakesThePickedCrewAndVehicle_MovesTheSchedule_AndKeepsTheCommittedEnd()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db); var backup = await f.AlternateAsync();
        var driver = await AddAsync(db, EmployeeRole.DRIVER); var escort = await AddAsync(db, EmployeeRole.ESCORT);
        var vehicle = new Vehicle { VehicleId = Guid.NewGuid(), VehicleCode = "V2", CapacityHorses = 5, TransportMode = TransportMode.ROAD,
            RegistrationExpiryDate = new(2031, 1, 1), InsuranceExpiryDate = new(2031, 1, 1) };
        db.Add(vehicle); await db.SaveChangesAsync();
        var row = await BackupRowAsync(db, backup);
        var committedEnd = await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync();

        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId, new()
        {
            Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), StartAt = Start.AddHours(4),
            Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, DriverEmployeeId = driver.EmployeeId,
                EscortEmployeeId = escort.EmployeeId, VehicleIds = [vehicle.VehicleId] }]
        });

        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal((driver.EmployeeId, escort.EmployeeId), (row.DriverEmployeeId, row.EscortEmployeeId));
        Assert.Equal(new[] { vehicle.VehicleId }, await db.RoutePlanRouteVehicles.Where(x => x.RoutePlanRouteId == row.RoutePlanRouteId).Select(x => x.VehicleId).ToArrayAsync());
        Assert.Equal((Start.AddHours(4).UtcDateTime, Start.AddHours(6).UtcDateTime), (row.PlannedDepartureAt, row.PlannedArrivalAt));
        Assert.Equal(committedEnd, await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync());
        Assert.Contains("You now run", await MessageAsync(db, driver));
        Assert.Contains("no further segment", await MessageAsync(db, f.Driver));
        Assert.Contains("no further segment", await MessageAsync(db, f.Escort));
    }

    [Fact]
    public async Task Approve_AnInternalSegmentWithNobodyToInherit_NeedsThePicksOfTheManager()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        // The primary leg is outsourced in practice (a flight): nobody of ours is on it.
        f.Current.DriverEmployeeId = f.Current.EscortEmployeeId = null; await db.SaveChangesAsync();
        var backup = await f.AlternateAsync();
        var row = await BackupRowAsync(db, backup);

        Assert.True(Assert.Single((await f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId)).Segments).NeedsCrew);
        var missing = await Assert.ThrowsAsync<ConflictException>(() => f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) }));
        Assert.Equal("RESOURCES_NOT_ASSIGNED", missing.ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, backup.Status);

        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId, new()
        {
            Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup),
            Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, DriverEmployeeId = f.Driver.EmployeeId, EscortEmployeeId = f.Escort.EmployeeId }]
        });
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal(f.Driver.EmployeeId, row.DriverEmployeeId);
    }

    [Theory]
    [InlineData("past", "INVALID_START_TIME")]
    [InlineData("shared", "SEGMENT_NOT_FOUND")]
    [InlineData("employee", "EMPLOYEE_NOT_FOUND")]
    public async Task Approve_RejectsABadStartTimeOrPick_WithoutChanges(string kind, string code)
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db); var backup = await f.AlternateAsync();
        var row = await BackupRowAsync(db, backup);
        // The row before the branch point is a copy of the primary: it is not part of the change.
        var shared = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == backup.RoutePlanId && x.RouteType == RouteType.PRIMARY);
        var request = new Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) };
        if (kind == "past") request.StartAt = Start.AddHours(-2);
        if (kind == "shared") request.Segments = [new() { RoutePlanRouteId = shared.RoutePlanRouteId, DriverEmployeeId = f.Driver.EmployeeId }];
        if (kind == "employee") request.Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, DriverEmployeeId = Guid.NewGuid() }];

        var error = await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() => f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId, request));

        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, backup.Status);
        Assert.Null(row.DriverEmployeeId);
    }

    [Fact]
    public async Task Approve_ABranchPointFurtherAhead_KeepsThePrimarySegmentsUpToIt()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var (finish, suffix) = await AddSuffixAsync(db, f);
        // The horse is still before Destination: the backup only replaces Destination -> Finish.
        var backup = NewBackup(db, f);
        AddRow(db, backup, f.Completed.RouteId, 1, RouteType.PRIMARY, Start.AddHours(-3));
        AddRow(db, backup, f.Current.RouteId, 2, RouteType.PRIMARY, Start);
        var detour = AddRow(db, backup, f.NewRoute(f.End, finish, 30).RouteId, 3, RouteType.BACKUP, Start.AddHours(3));
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = detour.RoutePlanRouteId, VehicleId = await db.Vehicles.Select(x => x.VehicleId).SingleAsync(), AssignedAt = Start.UtcDateTime });
        await db.SaveChangesAsync();

        var crew = Assert.Single((await f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId)).Segments);
        Assert.Equal(suffix.RoutePlanRouteId, crew.SuggestedFromRoutePlanRouteId);
        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) });

        Assert.Equal("ACTIVE", result.Status);
        Assert.Null(f.Current.SupersededAt); Assert.Equal(f.Driver.EmployeeId, f.Current.DriverEmployeeId);
        Assert.NotNull(suffix.SupersededAt);
        // The kept segment stays on the journey although the horse has not reached it yet.
        var plans = await db.RoutePlans.AsNoTracking().Include(x => x.RoutePlanRoutes).ToListAsync();
        Assert.Equal(new[] { f.Completed.RoutePlanRouteId, f.Current.RoutePlanRouteId, detour.RoutePlanRouteId },
            Pegaxus.Service.Common.TransportItinerary.Effective(plans).Select(x => x.Route.RoutePlanRouteId));
        // The personnel page lists the replaced primary segment as superseded, then the backup segment that took over.
        var assignments = await PersonnelAssignmentTests.CreateService(db).GetAsync(f.Plan.TransportPlanId, f.Manager.AccountId);
        Assert.Equal(new[] { (f.Completed.RoutePlanRouteId, "PRIMARY", false), (f.Current.RoutePlanRouteId, "PRIMARY", false),
                (suffix.RoutePlanRouteId, "PRIMARY", true), (detour.RoutePlanRouteId, "BACKUP", false) },
            assignments.Phase2.Segments.Select(x => (x.RoutePlanRouteId, x.RouteType, x.IsSuperseded)));
        Assert.Equal("Finish", assignments.Phase2.Segments[^1].ToLocationName);
    }

    [Theory]
    [InlineData("passed", "INCIDENT_LOCATION_REQUIRED")]
    [InlineData("elsewhere", "BRANCH_POINT_NOT_ON_ROUTE")]
    public async Task Change_TurningBackWithoutAnIncidentOrABranchPointOffTheRoute_IsRefusedEverywhere(string kind, string code)
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        await AddSuffixAsync(db, f);
        // The default backup branches at Safe stop, the start of the current segment.
        var backup = await f.AlternateAsync();
        if (kind == "passed") f.Current.ActualDepartureAt = Start.UtcDateTime;
        else
        {
            var elsewhere = new Location { LocationId = Guid.NewGuid(), LocationName = "Elsewhere", Country = "FR" };
            db.Add(elsewhere);
            (await BackupRowAsync(db, backup)).RouteId = f.NewRoute(elsewhere, f.End, 90).RouteId;
        }
        await db.SaveChangesAsync();

        Assert.Equal(code, (await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId))).ErrorCode);
        Assert.Equal(code, (await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            f.Service.CreateAsync(backup.RoutePlanId, f.Coordinator.AccountId, f.Proposal()))).ErrorCode);
        Assert.Equal(code, (await Assert.ThrowsAnyAsync<Pegaxus.Service.Exceptions.Exceptions>(() =>
            f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
                new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) }))).ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, backup.Status);
    }

    [Fact]
    public async Task Approve_PastEveryBranchPoint_TurnsBackFromTheIncident()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        // The default backup branches at S1, which the convoy left while running S1 -> S2.
        var backup = await f.AlternateAsync();
        var detour = await BackupRowAsync(db, backup);

        var crew = await f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId);
        Assert.Equal(("INCIDENT_LOCATION", true, f.Start.LocationId, trip.X.LocationId),
            (crew.AnchorMode, crew.TurnsBack, crew.StartLocation.LocationId, crew.EndedAtLocation!.LocationId));
        // The crew with the horse drives back.
        Assert.Equal(f.Current.RoutePlanRouteId, crew.Segments[0].SuggestedFromRoutePlanRouteId);

        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId, new()
            { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup), IncidentReportId = trip.Incident.IncidentReportId });

        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal(trip.X.LocationId, f.Current.EndedAtLocationId); Assert.NotNull(f.Current.ActualArrivalAt);
        Assert.NotNull(trip.Suffix.SupersededAt);
        Assert.NotNull(detour.ActualDepartureAt);
        var plans = await db.RoutePlans.AsNoTracking().Include(x => x.RoutePlanRoutes).ToListAsync();
        var journey = Pegaxus.Service.Common.TransportItinerary.Effective(plans).Select(x => x.Route.RoutePlanRouteId).ToList();
        Assert.Equal(new[] { f.Completed.RoutePlanRouteId, f.Current.RoutePlanRouteId, detour.RoutePlanRouteId }, journey.Take(3));
        Assert.Contains("Stop where the incident happened and turn back to Safe stop", await MessageAsync(db, f.Driver));
    }

    [Fact]
    public async Task Approve_WithSeveralBranchPoints_TakesTheNearestAhead_AndSkipsTheStretchAlreadyCovered()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        // Backup O -> S1 (shared), S1 -> Y -> S2 (first detour, behind the convoy), S2 -> D by another road (second detour, ahead).
        var y = new Location { LocationId = Guid.NewGuid(), LocationName = "Y", Country = "FR" };
        db.Add(y);
        var backup = NewBackup(db, f);
        AddRow(db, backup, f.Completed.RouteId, 1, RouteType.PRIMARY, Start.AddHours(-3));
        var first = AddRow(db, backup, f.NewRoute(f.Start, y, 30).RouteId, 2, RouteType.BACKUP, Start);
        var rejoin = AddRow(db, backup, f.NewRoute(y, f.End, 30).RouteId, 3, RouteType.BACKUP, Start.AddHours(1));
        var second = AddRow(db, backup, f.NewRoute(f.End, trip.D, 25).RouteId, 4, RouteType.BACKUP, Start.AddHours(3));
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = second.RoutePlanRouteId, VehicleId = await db.Vehicles.Select(x => x.VehicleId).SingleAsync(), AssignedAt = Start.UtcDateTime });
        await db.SaveChangesAsync();

        var crew = await f.Service.GetCrewPlanAsync(backup.RoutePlanId, f.Manager.AccountId);
        Assert.Equal(("NEXT_STOP", false, f.End.LocationId), (crew.AnchorMode, crew.TurnsBack, crew.StartLocation.LocationId));
        Assert.Equal(second.RoutePlanRouteId, Assert.Single(crew.Segments).Segment.RoutePlanRouteId);

        var result = await f.Service.DecideAsync(backup.RoutePlanId, f.Manager.AccountId,
            new() { Decision = "APPROVE", LastUpdatedAt = RouteChangeTests.Fixture.Version(backup) });

        Assert.Equal("ACTIVE", result.Status);
        Assert.Null(f.Current.ActualArrivalAt); Assert.Null(f.Current.SupersededAt);
        Assert.NotNull(trip.Suffix.SupersededAt); Assert.NotNull(first.SupersededAt); Assert.NotNull(rejoin.SupersededAt);
        var plans = await db.RoutePlans.AsNoTracking().Include(x => x.RoutePlanRoutes).ToListAsync();
        Assert.Equal(new[] { f.Completed.RoutePlanRouteId, f.Current.RoutePlanRouteId, second.RoutePlanRouteId },
            Pegaxus.Service.Common.TransportItinerary.Effective(plans).Select(x => x.Route.RoutePlanRouteId));
    }

    [Fact]
    public void Validator_TakesAStartTimeAndCrewOnlyWithAnApproval()
    {
        var validator = new DecisionRequestValidator();
        Assert.False(validator.Validate(new Request.DecisionRequest { Decision = "REJECT", Note = "Road reopened, no change", LastUpdatedAt = Start, StartAt = Start }).IsValid);
        Assert.False(validator.Validate(new Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = Start,
            Segments = [new() { RoutePlanRouteId = Guid.Empty }] }).IsValid);
        Assert.True(validator.Validate(new Request.DecisionRequest { Decision = "APPROVE", LastUpdatedAt = Start, StartAt = Start,
            Segments = [new() { RoutePlanRouteId = Guid.NewGuid(), DriverEmployeeId = Guid.NewGuid() }] }).IsValid);
    }

    private static async Task<Employee> AddAsync(AppDbContext db, EmployeeRole role)
    {
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        return employee;
    }

    /// <summary>An untravelled primary segment Destination -> Finish after the current one, with the same crew and vehicle.</summary>
    private static async Task<(Location Finish, RoutePlanRoute Suffix)> AddSuffixAsync(AppDbContext db, RouteChangeTests.Fixture f)
    {
        var finish = new Location { LocationId = Guid.NewGuid(), LocationName = "Finish", Country = "DE" };
        db.Add(finish);
        var suffix = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = f.Plan, Route = f.NewRoute(f.End, finish, 20), SequenceNo = 3,
            DriverEmployeeId = f.Driver.EmployeeId, EscortEmployeeId = f.Escort.EmployeeId,
            PlannedDepartureAt = Start.AddHours(3).UtcDateTime, PlannedArrivalAt = Start.AddHours(4).UtcDateTime };
        db.Add(suffix);
        db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = suffix.RoutePlanRouteId,
            VehicleId = await db.Vehicles.Select(x => x.VehicleId).SingleAsync(), AssignedAt = Start.UtcDateTime });
        await db.SaveChangesAsync();
        return (finish, suffix);
    }

    private static RoutePlan NewBackup(AppDbContext db, RouteChangeTests.Fixture f)
    {
        var at = Start.UtcDateTime;
        var plan = new RoutePlan { RoutePlanId = Guid.NewGuid(), TransportPlanId = f.Plan.TransportPlanId, PrimaryRoutePlanId = f.Plan.RoutePlanId,
            RoutePlanCode = "BACKUP-LATE", Status = RoutePlanStatus.CONFIRMED, SubmittedForReviewAt = at, CreatedAt = at, UpdatedAt = at,
            RouteNote = "Emergency route planning" };
        db.Add(plan);
        return plan;
    }

    private static RoutePlanRoute AddRow(AppDbContext db, RoutePlan plan, Guid routeId, int sequenceNo, RouteType type, DateTimeOffset departure)
    {
        var row = new RoutePlanRoute { RoutePlanRouteId = Guid.NewGuid(), RoutePlan = plan, RouteId = routeId, SequenceNo = sequenceNo, RouteType = type,
            PlannedDepartureAt = departure.UtcDateTime, PlannedArrivalAt = departure.AddHours(1).UtcDateTime };
        db.Add(row);
        return row;
    }

    private static Task<RoutePlanRoute> BackupRowAsync(AppDbContext db, RoutePlan backup) =>
        db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == backup.RoutePlanId && x.RouteType == RouteType.BACKUP);

    private static Task<string> MessageAsync(AppDbContext db, Employee employee) =>
        db.AccountNotifications.Where(x => x.AccountId == employee.AccountId && x.Notification.Title == "Backup route activated")
            .Select(x => x.Notification.Message).SingleAsync();
}
