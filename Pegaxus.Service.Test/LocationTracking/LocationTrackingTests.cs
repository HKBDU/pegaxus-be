using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.LocationTrackingService.Validator;
using Pegaxus.Service.NotificationService;

using Tracking = Pegaxus.Service.LocationTrackingService;

namespace Pegaxus.Service.Test;

public class LocationTrackingTests
{
    [Fact]
    public async Task Snapshot_UsesEffectiveRouteCurrentStopPlannedEtaAndLatestPointWithoutGpsHistory()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var before = await f.Service.GetAsync(f.TransportId, f.CustomerAccountId);
        Assert.Null(before.Truck); Assert.Equal(2, before.PlannedRoute.Segments.Count);
        Assert.Equal("IN_TRANSIT", before.TransportStatus); Assert.Equal(30, before.PlannedRemainingDistanceKm);
        Assert.Equal(f.First.Route.ToLocationId, before.NextStop!.LocationId);
        Assert.Equal(DbTime.AsUtc(f.Second.PlannedArrivalAt!.Value), before.EtaAt);
        Assert.Equal(new[] { "IN_TRANSIT", "PLANNED" }, before.PlannedRoute.Segments.Select(segment => segment.Status));
        var request = f.Point(0.5m); request.SpeedKmh = 78.5m; request.HeadingDegrees = 312; request.AccuracyMeters = 12;
        var accepted = await f.Service.RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Equal(f.Clock.Now, accepted.ServerTime); Assert.Single(f.Realtime.Points);
        var after = await f.Service.GetAsync(f.TransportId, f.Manager.AccountId);
        Assert.Equal(0.5m, after.Truck!.Longitude); Assert.Equal(10, after.Truck.Latitude);
        Assert.Equal(request.RecordedAt, after.Truck.RecordedAt); Assert.Equal(f.First.RoutePlanRouteId, after.Truck.RoutePlanRouteId);
        Assert.Equal(312, after.Truck.HeadingDegrees); Assert.Equal(78.5m, after.Truck.SpeedKmh); Assert.Equal(12, after.Truck.AccuracyMeters);
        Assert.Null((await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId)).ActualArrivalAt);
        Assert.Empty(await db.Notifications.ToListAsync()); Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(480, true)]
    [InlineData(520, false)]
    public async Task Geofence_MarksOnlyStartedSegmentInsideConfiguredRadius(double distance, bool arrived)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var departure = f.First.ActualDepartureAt;
        await f.Service.RecordAsync(f.TransportId, f.Point(1 + LongitudeOffset(distance)), f.Driver.AccountId);
        var first = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == f.First.RoutePlanRouteId);
        Assert.Equal(arrived, first.ActualArrivalAt.HasValue); Assert.Equal(departure, first.ActualDepartureAt);
        Assert.Null(f.Second.ActualDepartureAt); Assert.Null(f.Second.ActualArrivalAt);
        Assert.Null(f.Route.TransportPlan.Transport.ActualArrivalAt);
        Assert.Equal(TransportStatus.IN_TRANSIT, f.Route.TransportPlan.Transport.Status);
    }

    [Fact]
    public async Task Geofence_FinalArrivalIsAtomicAndNeverOverwritesRecordedTimesOrCompletesTransport()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        f.First.ActualArrivalAt = Fixture.Now.AddMinutes(-30); f.Second.ActualDepartureAt = Fixture.Now.AddMinutes(-20);
        await db.SaveChangesAsync();
        var firstArrival = f.First.ActualArrivalAt; var departure = f.Second.ActualDepartureAt;
        var request = f.Point(2);
        await f.Service.RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Equal(Fixture.Now, f.Second.ActualArrivalAt); Assert.Equal(Fixture.Now, f.Route.TransportPlan.Transport.ActualArrivalAt);
        Assert.Equal(firstArrival, f.First.ActualArrivalAt); Assert.Equal(departure, f.Second.ActualDepartureAt);
        Assert.Equal(RoutePlanStatus.ACTIVE, f.Route.Status); Assert.Equal(TransportStatus.IN_TRANSIT, f.Route.TransportPlan.Transport.Status);
        request.RecordedAt = request.RecordedAt!.Value.AddSeconds(15);
        await f.Service.RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Equal(Fixture.Now, f.Second.ActualArrivalAt); Assert.Equal(Fixture.Now, f.Route.TransportPlan.Transport.ActualArrivalAt);
        Assert.Null((await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).NextStop);
        Assert.Equal(0, (await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).PlannedRemainingDistanceKm);
    }

    [Fact]
    public async Task Geofence_RespectsCustomRadiusAndCannotArriveBeforeDepartureOrStartPlannedSegments()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var service = f.CreateService(db, radius: 100);
        await service.RecordAsync(f.TransportId, f.Point(1 + LongitudeOffset(115)), f.Driver.AccountId);
        Assert.Null(f.First.ActualArrivalAt);
        var request = f.Point(1); request.RecordedAt = DbTime.AsUtc(f.First.ActualDepartureAt!.Value.AddSeconds(-1));
        await f.CreateService(db, new Tracking.TrackingState()).RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Null(f.First.ActualArrivalAt);
        request = f.Point(2); request.RecordedAt = f.Clock.Now.AddSeconds(15);
        await service.RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Null(f.Second.ActualDepartureAt); Assert.Null(f.Second.ActualArrivalAt); Assert.Null(f.Route.TransportPlan.Transport.ActualArrivalAt);
        var previous = Fixture.Now.AddMinutes(-1); f.Route.TransportPlan.Transport.ActualArrivalAt = previous;
        f.First.ActualArrivalAt = previous; f.Second.ActualDepartureAt = previous;
        await db.SaveChangesAsync(); request.RecordedAt = f.Clock.Now.AddSeconds(30);
        await service.RecordAsync(f.TransportId, request, f.Driver.AccountId);
        Assert.Equal(previous, f.Route.TransportPlan.Transport.ActualArrivalAt);
    }

    [Fact]
    public async Task Samples_DuplicateOrOlderRequestsDoNotReplaceLatestOrRepeatEffects()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        await f.Service.RecordAsync(f.TransportId, f.Point(0.5m), f.Driver.AccountId);
        var older = f.Point(1); older.RecordedAt = older.RecordedAt!.Value.AddSeconds(-15);
        await f.Service.RecordAsync(f.TransportId, older, f.Driver.AccountId);
        await f.Service.RecordAsync(f.TransportId, f.Point(1), f.Driver.AccountId);
        Assert.Single(f.Realtime.Points); Assert.Null(f.First.ActualArrivalAt);
        Assert.Equal(0.5m, (await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).Truck!.Longitude);
        var newer = f.Point(1); newer.RecordedAt = newer.RecordedAt!.Value.AddSeconds(15);
        await f.Service.RecordAsync(f.TransportId, newer, f.Driver.AccountId);
        Assert.Equal(2, f.Realtime.Points.Count); Assert.Equal(DbTime.Truncate(newer.RecordedAt.Value), f.First.ActualArrivalAt);
    }

    [Fact]
    public async Task RealtimeFailure_PreservesAcceptedSnapshotAndArrival()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Realtime.Fail = true;
        await f.Service.RecordAsync(f.TransportId, f.Point(1), f.Driver.AccountId);
        Assert.Equal(Fixture.Now, f.First.ActualArrivalAt);
        Assert.Equal(1, (await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).Truck!.Longitude);
        Assert.Null((await f.CreateService(db, new Tracking.TrackingState()).GetAsync(f.TransportId, f.Manager.AccountId)).Truck);
    }

    [Theory]
    [InlineData("latitudeMissing")]
    [InlineData("longitudeMissing")]
    [InlineData("latitudeRange")]
    [InlineData("longitudeRange")]
    [InlineData("timeMissing")]
    [InlineData("timeDefault")]
    [InlineData("future")]
    [InlineData("speed")]
    [InlineData("heading")]
    [InlineData("accuracy")]
    [InlineData("emptySegment")]
    [InlineData("foreignSegment")]
    public async Task Validation_RejectsInvalidSamplesBeforePublishing(string scenario)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Point(0);
        switch (scenario)
        {
            case "latitudeMissing": request.Latitude = null; break;
            case "longitudeMissing": request.Longitude = null; break;
            case "latitudeRange": request.Latitude = 90.1m; break;
            case "longitudeRange": request.Longitude = -180.1m; break;
            case "timeMissing": request.RecordedAt = null; break;
            case "timeDefault": request.RecordedAt = default(DateTimeOffset); break;
            case "future": request.RecordedAt = f.Clock.Now.AddMinutes(5).AddTicks(1); break;
            case "speed": request.SpeedKmh = -1; break;
            case "heading": request.HeadingDegrees = 360; break;
            case "accuracy": request.AccuracyMeters = -1; break;
            case "emptySegment": request.RoutePlanRouteId = Guid.Empty; break;
            case "foreignSegment": request.RoutePlanRouteId = Guid.NewGuid(); break;
        }
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<BadRequestException>(() => f.Service.RecordAsync(f.TransportId, request, f.Driver.AccountId))).ErrorCode);
        Assert.Empty(f.Realtime.Points); Assert.Null(f.State.For(f.TransportId).Latest); Assert.Null(f.First.ActualArrivalAt);
    }

    [Fact]
    public async Task Validation_AcceptsCoordinatesAndTimeAtBoundaries()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); var request = f.Point(180);
        request.Latitude = -90; request.RecordedAt = f.Clock.Now.AddMinutes(5); request.SpeedKmh = 0; request.HeadingDegrees = 359; request.AccuracyMeters = 0;
        await f.Service.RecordAsync(f.TransportId, request, f.Driver.AccountId); Assert.Single(f.Realtime.Points);
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, true)]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, true)]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, true)]
    [InlineData(EmployeeRole.DRIVER, false)]
    [InlineData(EmployeeRole.ESCORT, false)]
    public async Task Viewer_UsesActualDatabaseRoleAndHidesOtherCustomersTransports(EmployeeRole role, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var actor = await PersonnelAssignmentTests.EmployeeAsync(db, role);
        if (allowed) await f.Service.EnsureViewerAsync(f.TransportId, actor.AccountId);
        else await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.EnsureViewerAsync(f.TransportId, actor.AccountId));
        var (stranger, _) = await TestSupport.AddCustomerAsync(db);
        Assert.Equal("TRANSPORT_NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(f.TransportId, stranger.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(Guid.NewGuid(), f.Manager.AccountId));
        actor.IsActive = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => f.Service.EnsureViewerAsync(f.TransportId, actor.AccountId));
    }

    [Theory]
    [InlineData("wrongRole", false)]
    [InlineData("unassigned", false)]
    [InlineData("superseded", false)]
    [InlineData("executed", true)]
    [InlineData("outsourced", false)]
    [InlineData("draft", false)]
    public async Task Driver_RequiresLiveInternalAssignmentAndActualRole(string scenario, bool allowed)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Second.DriverEmployeeId = null;
        if (scenario == "wrongRole") f.Driver.Role = EmployeeRole.ESCORT;
        if (scenario == "unassigned") f.First.DriverEmployeeId = null;
        if (scenario is "superseded" or "executed") f.First.SupersededAt = Fixture.Now;
        if (scenario == "superseded") f.First.ActualDepartureAt = null;
        if (scenario == "outsourced") { f.First.TransportProvider = PersonnelAssignmentTests.Provider(); db.Add(f.First.TransportProvider); }
        if (scenario == "draft") f.Route.Status = RoutePlanStatus.DRAFT;
        await db.SaveChangesAsync();
        if (allowed) await f.Service.RecordAsync(f.TransportId, f.Point(0), f.Driver.AccountId);
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.RecordAsync(f.TransportId, f.Point(0), f.Driver.AccountId));
            await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId));
            Assert.Empty(f.Realtime.Points); Assert.Empty(db.Notifications);
        }
    }

    [Theory]
    [InlineData(TransportStatus.CREATED, false)]
    [InlineData(TransportStatus.PLANNING, false)]
    [InlineData(TransportStatus.READY, true)]
    [InlineData(TransportStatus.COMPLETED, true)]
    [InlineData(TransportStatus.CANCELLED, false)]
    public async Task Lifecycle_ReadAndWriteStatusesFollowContract(TransportStatus status, bool canRead)
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db); f.Route.TransportPlan.Transport.Status = status; await db.SaveChangesAsync();
        if (canRead) await f.Service.GetAsync(f.TransportId, f.Manager.AccountId);
        else await Assert.ThrowsAsync<ConflictException>(() => f.Service.GetAsync(f.TransportId, f.Manager.AccountId));
        Assert.Equal("INVALID_TRANSPORT_STATUS", (await Assert.ThrowsAsync<ConflictException>(() => f.Service.RecordAsync(f.TransportId, f.Point(0), f.Driver.AccountId))).ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId));
    }

    [Fact]
    public async Task PermissionDenied_QueuesOnlyAssignedActiveCoordinatorsAndThrottlesPerDriverForTenMinutes()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var other = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
        var inactive = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR); inactive.Account.Status = AccountStatus.INACTIVE;
        db.Add(new EmployeeTransportPlan { Employee = inactive, TransportPlan = f.Route.TransportPlan, Responsibility = "FLEET_ROUTE_COORDINATOR" });
        await db.SaveChangesAsync();
        Assert.True((await f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.TRANSPORT, notification.NotificationType); Assert.Equal(NotificationPriority.HIGH, notification.Priority);
        Assert.Equal("Driver GPS sharing is disabled", notification.Title); Assert.Contains(f.Driver.FullName, notification.Message);
        Assert.Equal("Transport", notification.ReferenceType); Assert.Equal(f.TransportId, notification.ReferenceId);
        Assert.Equal($"/admin/ongoing-transports/{f.TransportId}", notification.ActionUrl);
        Assert.Equal(f.Coordinator.AccountId, (await db.AccountNotifications.SingleAsync()).AccountId);
        Assert.False((await f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
        f.Clock.Now = f.Clock.Now.AddMinutes(10).AddTicks(-1);
        Assert.False((await f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
        f.Clock.Now = f.Clock.Now.AddTicks(1);
        Assert.True((await f.Service.PermissionDeniedAsync(f.TransportId, f.Driver.AccountId)).Notified);
        var secondDriver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER); f.Second.DriverEmployeeId = secondDriver.EmployeeId; await db.SaveChangesAsync();
        Assert.True((await f.Service.PermissionDeniedAsync(f.TransportId, secondDriver.AccountId)).Notified);
        Assert.Equal(3, await db.Notifications.CountAsync()); Assert.Equal(3, f.NotificationRealtime.Messages.Count);
        Assert.Null(f.State.For(f.TransportId).Latest); Assert.Empty(f.Realtime.Points);
    }

    [Fact]
    public async Task EffectiveRoute_ActivatesBackupAtItsBranchAndPreservesExecutedHistory()
    {
        await using var db = TestSupport.CreateDb(); var f = await Fixture.CreateAsync(db);
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "BACKUP", PrimaryRoutePlanId = f.Route.RoutePlanId,
            TransportPlan = f.Route.TransportPlan, Status = RoutePlanStatus.CONFIRMED, UpdatedAt = Fixture.Now.AddMinutes(1) };
        var copied = PersonnelAssignmentTests.Segment(backup, 1); copied.Route = f.First.Route;
        var branch = PersonnelAssignmentTests.Segment(backup, 2); branch.RouteType = RouteType.BACKUP;
        var tail = PersonnelAssignmentTests.Segment(backup, 3); tail.Route.DistanceKm = 7;
        db.AddRange(copied, branch, tail); await db.SaveChangesAsync();
        Assert.Equal(new[] { f.First.RoutePlanRouteId, f.Second.RoutePlanRouteId },
            (await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).PlannedRoute.Segments.Select(segment => segment.RoutePlanRouteId));
        f.Second.SupersededAt = Fixture.Now; backup.Status = RoutePlanStatus.ACTIVE; await db.SaveChangesAsync();
        var snapshot = await f.Service.GetAsync(f.TransportId, f.Manager.AccountId);
        Assert.Equal(new[] { f.First.RoutePlanRouteId, branch.RoutePlanRouteId, tail.RoutePlanRouteId }, snapshot.PlannedRoute.Segments.Select(segment => segment.RoutePlanRouteId));
        Assert.Equal(new[] { 1, 2, 3 }, snapshot.PlannedRoute.Segments.Select(segment => segment.DisplayOrder));
        Assert.Equal(17, snapshot.PlannedRemainingDistanceKm);
        branch.RouteType = RouteType.RETURN; copied.RouteType = RouteType.RETURN; tail.RouteType = RouteType.RETURN; await db.SaveChangesAsync();
        Assert.Equal(new[] { f.First.RoutePlanRouteId, copied.RoutePlanRouteId, branch.RoutePlanRouteId, tail.RoutePlanRouteId },
            (await f.Service.GetAsync(f.TransportId, f.Manager.AccountId)).PlannedRoute.Segments.Select(segment => segment.RoutePlanRouteId));
    }

    [Fact]
    public void Geo_DistanceHandlesSamePointAntipodesAndDateLine()
    {
        Assert.Equal(0, Geo.DistanceMeters(10, 20, 10, 20));
        Assert.InRange(Geo.DistanceMeters(0, 0, 0, 180), 20_015_000, 20_016_000);
        Assert.InRange(Geo.DistanceMeters(0, 179.999m, 0, -179.999m), 222, 223);
    }

    // Coordinates carry at most 4 decimals (about 11 m), so the offset is rounded the way a client would send it.
    private static decimal LongitudeOffset(double meters) =>
        Math.Round((decimal)(meters / (6_371_000 * Math.Cos(FixtureLatitude * Math.PI / 180)) * 180 / Math.PI), 4);

    private const int FixtureLatitude = 10;

    internal sealed class Fixture(AppDbContext db)
    {
        internal static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Unspecified);
        internal required Employee Manager { get; init; }
        internal required Employee Coordinator { get; init; }
        internal required Employee Driver { get; init; }
        internal required Guid CustomerAccountId { get; init; }
        internal required RoutePlan Route { get; init; }
        internal required RoutePlanRoute First { get; init; }
        internal required RoutePlanRoute Second { get; init; }
        internal Guid TransportId => Route.TransportPlan.TransportId;
        internal Clock Clock { get; } = new();
        internal Tracking.TrackingState State { get; } = new();
        internal Realtime Realtime { get; } = new();
        internal NotificationRealtime NotificationRealtime { get; } = new();
        internal Tracking.Service Service => CreateService(db);
        internal Tracking.Request.LocationRequest Point(decimal longitude) => new() { Latitude = FixtureLatitude, Longitude = longitude, RecordedAt = Clock.Now };
        internal Tracking.Service CreateService(AppDbContext context, Tracking.TrackingState? state = null, double radius = 500) => new(context,
            Clock, state ?? State, Options.Create(new Tracking.TrackingOptions { GeofenceRadiusMeters = radius }), new LocationRequestValidator(Clock), Realtime,
            new Pegaxus.Service.NotificationService.Service(context, NotificationRealtime), NullLogger<Tracking.Service>.Instance);
        internal static async Task<Fixture> CreateAsync(AppDbContext db)
        {
            var manager = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
            var coordinator = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.FLEET_ROUTE_COORDINATOR);
            var driver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
            var (route, first) = await PersonnelAssignmentTests.RouteAsync(db, RoutePlanStatus.ACTIVE);
            route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
            route.UpdatedAt = Now;
            first.Route.FromLocation.Latitude = FixtureLatitude; first.Route.FromLocation.Longitude = 0;
            first.Route.ToLocation.Latitude = FixtureLatitude; first.Route.ToLocation.Longitude = 1;
            first.Route.DistanceKm = 10; first.DriverEmployeeId = driver.EmployeeId; first.ActualDepartureAt = Now.AddHours(-1); first.PlannedArrivalAt = Now.AddHours(1);
            var second = PersonnelAssignmentTests.Segment(route, 2); second.Route.FromLocation = first.Route.ToLocation;
            second.Route.ToLocation.Latitude = FixtureLatitude; second.Route.ToLocation.Longitude = 2; second.DriverEmployeeId = driver.EmployeeId;
            second.Route.DistanceKm = 20; second.PlannedArrivalAt = Now.AddHours(2);
            db.Add(second); db.Add(new EmployeeTransportPlan { Employee = coordinator, TransportPlan = route.TransportPlan, Responsibility = "FLEET_ROUTE_COORDINATOR" });
            await db.SaveChangesAsync();
            var customer = await db.Bookings.Where(booking => booking.BookingId == route.TransportPlan.Transport.BookingId).Select(booking => booking.Customer.AccountId).SingleAsync();
            return new(db) { Manager = manager, Coordinator = coordinator, Driver = driver, CustomerAccountId = customer, Route = route, First = first, Second = second };
        }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DbTime.AsUtc(Fixture.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Realtime : Tracking.ILocationTrackingRealtimeService
    {
        internal List<Tracking.Response.LocationUpdate> Points { get; } = [];
        internal List<Guid> Viewers { get; private set; } = [];
        internal bool Fail { get; set; }
        public Task PublishAsync(Tracking.Response.LocationUpdate location, IReadOnlyList<Guid> viewerAccountIds, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Planned stream failure");
            Viewers = viewerAccountIds.ToList(); Points.Add(location); return Task.CompletedTask;
        }
    }
    internal sealed class NotificationRealtime : INotificationRealtimeService
    {
        internal List<NotificationRealtimeDto> Messages { get; } = [];
        public Task SendToAccountAsync(Guid accountId, NotificationRealtimeDto notification, CancellationToken cancellationToken = default)
        { Messages.Add(notification); return Task.CompletedTask; }
    }
}
