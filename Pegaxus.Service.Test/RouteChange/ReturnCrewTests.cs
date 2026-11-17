using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

/// <summary>A return activated during a real trip: at the incident, at the next stop, or refused when it does not join the trip.</summary>
public class ReturnCrewTests
{
    private static readonly DateTimeOffset Start = TripFixture.Start;

    [Fact]
    public async Task AtTheIncident_TheRunningSegmentStopsThere_AndTheReturnStartsAtOnce()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        var plan = await trip.ReturnAsync(db, Start.AddHours(1), trip.X, f.Start, f.Origin);

        var crew = await f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId);
        Assert.Equal(("INCIDENT_LOCATION", trip.X.LocationId, trip.X.LocationId), (crew.AnchorMode, crew.StartLocation.LocationId, crew.EndedAtLocation!.LocationId));
        Assert.Equal(f.Current.RoutePlanRouteId, crew.Segments[0].SuggestedFromRoutePlanRouteId);
        Assert.Equal(f.Driver.EmployeeId, crew.Segments[0].Segment.Driver!.EmployeeId);

        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, Activation(db, plan));

        Assert.Equal("ACTIVE", result.Status);
        Assert.NotNull(f.Current.ActualArrivalAt); Assert.Equal(trip.X.LocationId, f.Current.EndedAtLocationId);
        Assert.NotNull(trip.Suffix.SupersededAt); Assert.Null(f.Current.SupersededAt);
        var detail = await TestSupport.CreateRoutePlans(db).GetByIdAsync(f.Manager.AccountId, f.Plan.RoutePlanId);
        Assert.Equal("Incident spot", detail.Segments.Single(x => x.RoutePlanRouteId == f.Current.RoutePlanRouteId).EndedAtLocation!.LocationName);
        var returning = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == plan.RoutePlanId).OrderBy(x => x.SequenceNo).ToListAsync();
        // The same driver goes on, so no handover is needed: the first return segment starts now.
        Assert.NotNull(returning[0].ActualDepartureAt);
        Assert.Equal(new[] { f.Completed.RoutePlanRouteId, f.Current.RoutePlanRouteId }.Concat(returning.Select(x => x.RoutePlanRouteId)),
            await EffectiveAsync(db));
    }

    [Fact]
    public async Task AtTheNextStop_TheRunningSegmentGoesOn_AndTheReturnCannotStartBeforeItArrives()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        var plan = await trip.ReturnAsync(db, Start.AddHours(2), f.End, f.Start, f.Origin);

        var crew = await f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId);
        Assert.Equal(("NEXT_STOP", f.End.LocationId), (crew.AnchorMode, crew.StartLocation.LocationId));
        Assert.Null(crew.EndedAtLocation);
        var early = Activation(db, plan); early.StartAt = Start.AddHours(1);
        Assert.Equal("INVALID_START_TIME", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, early))).ErrorCode);

        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, Activation(db, plan));

        Assert.Equal("ACTIVE", result.Status);
        Assert.Null(f.Current.ActualArrivalAt); Assert.Null(f.Current.EndedAtLocationId);
        Assert.NotNull(trip.Suffix.SupersededAt);
        var returning = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == plan.RoutePlanId).OrderBy(x => x.SequenceNo).Select(x => x.RoutePlanRouteId).ToListAsync();
        Assert.Equal(new[] { f.Completed.RoutePlanRouteId, f.Current.RoutePlanRouteId }.Concat(returning), await EffectiveAsync(db));
    }

    [Theory]
    [InlineData("left")][InlineData("elsewhere")][InlineData("resolved")][InlineData("cancelled")]
    public async Task AReturnThatDoesNotJoinTheTrip_IsRefused(string kind)
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        var elsewhere = new Location { LocationId = Guid.NewGuid(), LocationName = "Elsewhere", Country = "FR" };
        db.Add(elsewhere);
        if (kind == "resolved") trip.Incident.Status = IncidentStatus.RESOLVED;
        if (kind == "cancelled") trip.Incident.Status = IncidentStatus.CANCELLED;
        await db.SaveChangesAsync();
        var plan = kind switch
        {
            // From S1, which the convoy has already left.
            "left" => await trip.ReturnAsync(db, Start.AddHours(1), f.Start, f.Origin),
            "elsewhere" => await trip.ReturnAsync(db, Start.AddHours(1), elsewhere, f.Start, f.Origin),
            _ => await trip.ReturnAsync(db, Start.AddHours(1), trip.X, f.Start, f.Origin)
        };

        Assert.Equal("RETURN_NOT_ANCHORED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId))).ErrorCode);
        Assert.Equal("RETURN_NOT_ANCHORED", (await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, Activation(db, plan)))).ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, plan.Status); Assert.Null(f.Current.ActualArrivalAt);
    }

    [Fact]
    public async Task WhileTheTransportIsOnHold_TheReturnStillActivates()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        // A hold is only a notification with an expiry: it never touches the segments.
        db.Notifications.Add(new() { NotificationId = Guid.NewGuid(), TransportId = f.Transport.TransportId, NotificationType = NotificationType.ROUTE,
            Priority = NotificationPriority.URGENT, Title = "Transport hold decision", Message = "{}", ReferenceType = "IncidentHold",
            ReferenceId = trip.Incident.IncidentReportId, CreatedAt = Start.UtcDateTime, ExpiresAt = Start.AddHours(3).UtcDateTime });
        var plan = await trip.ReturnAsync(db, Start.AddHours(1), trip.X, f.Start, f.Origin);

        Assert.Equal("ACTIVE", (await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, Activation(db, plan))).Status);
    }

    [Fact]
    public async Task Activate_TakesThePickedCrew_MovesTheSchedule_AndTellsEachCrewMember()
    {
        await using var db = TestSupport.CreateDb(); var trip = await TripFixture.CreateAsync(db); var f = trip.F;
        var plan = await trip.ReturnAsync(db, Start.AddHours(1), trip.X, f.Start, f.Origin);
        var driver = await AddAsync(db, EmployeeRole.DRIVER); var escort = await AddAsync(db, EmployeeRole.ESCORT);
        var committedEnd = await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync();
        var request = Activation(db, plan); request.StartAt = Start.AddHours(5);
        foreach (var segment in request.Segments!) { segment.DriverEmployeeId = driver.EmployeeId; segment.EscortEmployeeId = escort.EmployeeId; }

        await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, request);

        var rows = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == plan.RoutePlanId).OrderBy(x => x.SequenceNo).ToListAsync();
        Assert.All(rows, row => Assert.Equal((driver.EmployeeId, escort.EmployeeId), (row.DriverEmployeeId, row.EscortEmployeeId)));
        Assert.Equal((Start.AddHours(5).UtcDateTime, Start.AddHours(6).UtcDateTime), (rows[0].PlannedDepartureAt, rows[0].PlannedArrivalAt));
        // A new driver takes over at the incident, so the first return segment waits for the handover.
        Assert.Null(rows[0].ActualDepartureAt);
        Assert.Equal(committedEnd, await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync());
        Assert.Contains("You now run return route plan", await MessageAsync(db, driver));
        Assert.Contains("Stop where the incident happened: the convoy follows return route plan", await MessageAsync(db, f.Driver));
        Assert.Contains("Hand over the horses there", await MessageAsync(db, f.Driver));
    }

    private static Pegaxus.Service.RouteChangeService.Request.ActivationRequest Activation(AppDbContext db, RoutePlan plan)
    {
        var vehicleId = db.Vehicles.Select(x => x.VehicleId).Single();
        return new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(plan),
            Segments = db.RoutePlanRoutes.Where(x => x.RoutePlanId == plan.RoutePlanId).OrderBy(x => x.SequenceNo)
                .Select(x => new Pegaxus.Service.RouteChangeService.Request.SegmentRequest { RoutePlanRouteId = x.RoutePlanRouteId, VehicleIds = new() { vehicleId } }).ToList() };
    }

    private static async Task<List<Guid>> EffectiveAsync(AppDbContext db) =>
        TransportItinerary.Effective(await db.RoutePlans.AsNoTracking().Include(x => x.RoutePlanRoutes).ToListAsync())
            .Select(x => x.Route.RoutePlanRouteId).ToList();

    private static async Task<Employee> AddAsync(AppDbContext db, EmployeeRole role)
    {
        var (_, employee) = await TestSupport.AddLogisticsManagerAsync(db);
        employee.Role = role;
        await db.SaveChangesAsync();
        return employee;
    }

    private static Task<string> MessageAsync(AppDbContext db, Employee employee) =>
        db.AccountNotifications.Where(x => x.AccountId == employee.AccountId && x.Notification.Title == "Return route activated")
            .Select(x => x.Notification.Message).SingleAsync();
}
