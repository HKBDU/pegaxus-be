using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Test.PersonnelAssignmentTests;

namespace Pegaxus.Service.Test;

/// <summary>On the road the manager may replace the crew of a segment that has not left yet; everything else stays locked.</summary>
public class PersonnelMidTripReplaceTests
{
    private sealed record Trip(RoutePlan Route, RoutePlanRoute Running, RoutePlanRoute Next, Employee Manager, Employee Driver, Employee Escort,
        Employee NextDriver, Employee NextEscort, Employee Spare);

    /// <summary>A primary on the road: segment 1 is running, segment 2 has not left. Both have a crew.</summary>
    private static async Task<Trip> TripAsync(AppDbContext db)
    {
        var manager = await EmployeeAsync(db, EmployeeRole.LOGISTICS_MANAGER);
        var driver = await EmployeeAsync(db, EmployeeRole.DRIVER); var escort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var nextDriver = await EmployeeAsync(db, EmployeeRole.DRIVER); var nextEscort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var spare = await EmployeeAsync(db, EmployeeRole.DRIVER);
        var (route, running) = await RouteAsync(db, RoutePlanStatus.ACTIVE);
        var next = Segment(route, 2);
        db.Add(next);
        route.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS;
        route.TransportPlan.Transport.Status = TransportStatus.IN_TRANSIT;
        running.ActualDepartureAt = Now.AddHours(-1);
        running.RouteType = next.RouteType = RouteType.PRIMARY;
        (running.DriverEmployeeId, running.EscortEmployeeId) = (driver.EmployeeId, escort.EmployeeId);
        (next.DriverEmployeeId, next.EscortEmployeeId) = (nextDriver.EmployeeId, nextEscort.EmployeeId);
        await db.SaveChangesAsync();
        return new(route, running, next, manager, driver, escort, nextDriver, nextEscort, spare);
    }

    private static Pegaxus.Service.PersonnelAssignmentService.Request.ReplaceRequest Replace(RoutePlan route, Employee? driver = null, Employee? escort = null) =>
        new() { LastUpdatedAt = Version(route), DriverEmployeeId = driver?.EmployeeId, EscortEmployeeId = escort?.EmployeeId, Reason = "Driver held up on another trip" };

    [Fact]
    public async Task Replace_OnTheRoad_ChangesASegmentThatHasNotLeft_AndKeepsTheRunningOneLocked()
    {
        await using var db = TestSupport.CreateDb(); var t = await TripAsync(db); var service = CreateService(db);

        var result = await service.ReplaceAsync(t.Route.RoutePlanId, t.Next.RoutePlanRouteId, Replace(t.Route, t.Spare), t.Manager.AccountId);

        Assert.Equal(t.Spare.EmployeeId, result.Segment.Driver!.EmployeeId);
        Assert.Equal(t.Route.RoutePlanId, result.Segment.RoutePlanId);
        var audit = await db.CatalogAuditLogs.SingleAsync(x => x.ActionType == "PERSONNEL_REPLACED");
        Assert.Equal("Driver held up on another trip", audit.Summary);
        Assert.Contains(await db.Notifications.Select(x => x.Title).ToListAsync(), title => title == "Personnel replaced");

        var running = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReplaceAsync(t.Route.RoutePlanId, t.Running.RoutePlanRouteId, Replace(t.Route, t.Spare), t.Manager.AccountId));
        Assert.Equal("SEGMENT_LOCKED", running.ErrorCode);
    }

    [Fact]
    public async Task Replace_OnTheRoad_StillRefusesTheFirstAssignment_AndAFinishedTrip()
    {
        await using var db = TestSupport.CreateDb(); var t = await TripAsync(db); var service = CreateService(db);

        var first = await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(t.Route.RoutePlanId,
            Assign(t.Route, t.Next, t.Spare, t.NextEscort), t.Manager.AccountId));
        Assert.Equal("PLAN_LOCKED", first.ErrorCode);

        t.Route.TransportPlan.Transport.Status = TransportStatus.COMPLETED; await db.SaveChangesAsync();
        var finished = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReplaceAsync(t.Route.RoutePlanId, t.Next.RoutePlanRouteId, Replace(t.Route, t.Spare), t.Manager.AccountId));
        Assert.Equal("PLAN_LOCKED", finished.ErrorCode);
    }

    [Fact]
    public async Task Replace_RefusesANewDriverWhileAHandoverOfTheSegmentIsOpen_ButAllowsANewEscort()
    {
        await using var db = TestSupport.CreateDb(); var t = await TripAsync(db); var service = CreateService(db);
        db.HandoverRecords.Add(new HandoverRecord
        {
            HandoverRecordId = Guid.NewGuid(), TransportId = t.Route.TransportPlan.TransportId, HandoverCode = "HO-OPEN", SequenceNo = 2,
            HandoverType = HandoverType.TRANSFER, Status = HandoverStatus.IN_PROGRESS, FromType = HandoverPartyType.EMPLOYEE, ToType = HandoverPartyType.EMPLOYEE,
            FromEmployeeId = t.Driver.EmployeeId, ToEmployeeId = t.NextDriver.EmployeeId,
            FromRoutePlanRouteId = t.Running.RoutePlanRouteId, ToRoutePlanRouteId = t.Next.RoutePlanRouteId, CreatedAt = Now, UpdatedAt = Now
        });
        await db.SaveChangesAsync();

        var open = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReplaceAsync(t.Route.RoutePlanId, t.Next.RoutePlanRouteId, Replace(t.Route, t.Spare), t.Manager.AccountId));
        Assert.Equal("HANDOVER_OPEN", open.ErrorCode);
        Assert.Contains("HO-OPEN", open.Message);

        var spareEscort = await EmployeeAsync(db, EmployeeRole.ESCORT);
        var escortOnly = await service.ReplaceAsync(t.Route.RoutePlanId, t.Next.RoutePlanRouteId, Replace(t.Route, escort: spareEscort), t.Manager.AccountId);
        Assert.Equal(spareEscort.EmployeeId, escortOnly.Segment.Escort!.EmployeeId);
    }

    [Fact]
    public async Task Replace_AfterABackupTookOver_ChangesTheBackupSegment_AndRefusesTheReplacedPrimarySegment()
    {
        await using var db = TestSupport.CreateDb(); var t = await TripAsync(db); var service = CreateService(db);
        t.Next.SupersededAt = Now;
        var backup = new RoutePlan { RoutePlanId = Guid.NewGuid(), RoutePlanCode = "RP-BK-" + Guid.NewGuid().ToString("N"), TransportPlan = t.Route.TransportPlan,
            PrimaryRoutePlanId = t.Route.RoutePlanId, Status = RoutePlanStatus.ACTIVE, CreatedAt = Now, UpdatedAt = Now };
        var detour = Segment(backup, 2);
        detour.RouteType = RouteType.BACKUP;
        (detour.DriverEmployeeId, detour.EscortEmployeeId) = (t.NextDriver.EmployeeId, t.NextEscort.EmployeeId);
        db.Add(detour);
        await db.SaveChangesAsync();

        var replaced = await Assert.ThrowsAsync<UnprocessableEntityException>(() =>
            service.ReplaceAsync(t.Route.RoutePlanId, t.Next.RoutePlanRouteId, Replace(t.Route, t.Spare), t.Manager.AccountId));
        Assert.Equal("SEGMENT_NOT_IN_ITINERARY", replaced.ErrorCode);

        var onBackup = await service.ReplaceAsync(backup.RoutePlanId, detour.RoutePlanRouteId, Replace(backup, t.Spare), t.Manager.AccountId);
        Assert.Equal(t.Spare.EmployeeId, onBackup.Segment.Driver!.EmployeeId);
        Assert.Equal(backup.RoutePlanId, onBackup.Segment.RoutePlanId);

        // The page lists the backup segment with the plan and version to change it through.
        var page = await service.GetAsync(t.Route.TransportPlanId, t.Manager.AccountId);
        var row = page.Phase2.Segments.Single(x => x.RoutePlanRouteId == detour.RoutePlanRouteId);
        Assert.Equal(backup.RoutePlanId, row.RoutePlanId);
        Assert.NotNull(row.RoutePlanUpdatedAt);
    }
}
