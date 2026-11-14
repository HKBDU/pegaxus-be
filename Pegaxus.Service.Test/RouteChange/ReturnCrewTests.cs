using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

/// <summary>The manager chooses the return crew, vehicles and start time when activating the return.</summary>
public class ReturnCrewTests
{
    private static readonly DateTimeOffset Start = RouteChangeTests.Fixture.StartAt;

    [Fact]
    public async Task CrewPlan_SuggestsTheCrewAtTheCurrentStop_NotTheCrewOfTheFirstSegment()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        // The first segment was driven by other people, who finished at Safe stop long ago.
        var earlyDriver = await AddAsync(db, EmployeeRole.DRIVER); var earlyEscort = await AddAsync(db, EmployeeRole.ESCORT);
        f.Completed.DriverEmployeeId = earlyDriver.EmployeeId; f.Completed.EscortEmployeeId = earlyEscort.EmployeeId;
        var plan = await ReadyReturnAsync(db, f);

        var crew = Assert.Single((await f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId)).Segments);

        Assert.Equal((f.Driver.EmployeeId, f.Escort.EmployeeId), (crew.Segment.Driver!.EmployeeId, crew.Segment.Escort!.EmployeeId));
        Assert.Equal(f.Current.RoutePlanRouteId, crew.SuggestedFromRoutePlanRouteId);
        Assert.False(crew.NeedsCrew);
    }

    [Fact]
    public async Task CrewPlan_WithNobodyWaiting_SuggestsTheCrewThatBroughtTheHorse()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        f.Completed.DriverEmployeeId = f.Driver.EmployeeId; f.Completed.EscortEmployeeId = f.Escort.EmployeeId;
        f.Current.DriverEmployeeId = f.Current.EscortEmployeeId = null;
        var plan = await ReadyReturnAsync(db, f);

        var crew = Assert.Single((await f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId)).Segments);

        Assert.Equal(f.Completed.RoutePlanRouteId, crew.SuggestedFromRoutePlanRouteId);
        Assert.Equal(f.Driver.EmployeeId, crew.Segment.Driver!.EmployeeId);
    }

    [Fact]
    public async Task Activate_TakesThePickedCrew_MovesTheSchedule_AndTellsEachCrewMember()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var plan = await ReadyReturnAsync(db, f);
        var driver = await AddAsync(db, EmployeeRole.DRIVER); var escort = await AddAsync(db, EmployeeRole.ESCORT);
        var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanId == plan.RoutePlanId);
        var committedEnd = await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync();

        var result = await f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, new()
        {
            LastUpdatedAt = RouteChangeTests.Fixture.Version(plan), StartAt = Start.AddHours(5),
            Segments = [new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleIds = [await db.Vehicles.Select(x => x.VehicleId).SingleAsync()],
                DriverEmployeeId = driver.EmployeeId, EscortEmployeeId = escort.EmployeeId }]
        });

        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal((driver.EmployeeId, escort.EmployeeId), (row.DriverEmployeeId, row.EscortEmployeeId));
        Assert.Equal((Start.AddHours(5).UtcDateTime, Start.AddHours(7).UtcDateTime), (row.PlannedDepartureAt, row.PlannedArrivalAt));
        Assert.Equal(committedEnd, await db.TransportPlans.Select(x => x.PlannedEndAt).SingleAsync());
        Assert.Contains("You now run return route plan", await MessageAsync(db, driver));
        Assert.Contains("no further segment", await MessageAsync(db, f.Driver));
    }

    [Fact]
    public async Task CrewPlanAndActivation_WaitUntilTheHorseReachesAStop()
    {
        await using var db = TestSupport.CreateDb();
        var f = await RouteChangeTests.Fixture.CreateAsync(db);
        var plan = await ReadyReturnAsync(db, f);
        f.Current.ActualDepartureAt = Start.UtcDateTime; await db.SaveChangesAsync();

        Assert.Equal("SEGMENT_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.GetReturnCrewPlanAsync(plan.RoutePlanId, f.Manager.AccountId))).ErrorCode);
        Assert.Equal("SEGMENT_LOCKED", (await Assert.ThrowsAsync<ConflictException>(() =>
            f.Service.ActivateReturnAsync(plan.RoutePlanId, f.Manager.AccountId, new() { LastUpdatedAt = RouteChangeTests.Fixture.Version(plan) }))).ErrorCode);
        Assert.Equal(RoutePlanStatus.CONFIRMED, plan.Status);
    }

    /// <summary>A RETURN Safe stop -> Origin, legally confirmed, with an approved extra dossier.</summary>
    private static async Task<RoutePlan> ReadyReturnAsync(AppDbContext db, RouteChangeTests.Fixture f)
    {
        var plan = await f.AlternateAsync(RouteType.RETURN, RoutePlanStatus.CONFIRMED);
        await f.AddExtraAsync();
        return plan;
    }

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
