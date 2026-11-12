using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

/// <summary>Each segment has its own crew: only the crew that has the horses now acts on the trip, plus the reporter on its own incident.</summary>
public class IncidentCrewTurnTests
{
    private sealed record Trip(VehicleIncidentTests.Scenario F, RoutePlanRoute Second, Employee NextDriver, Employee NextEscort);

    private static async Task<Trip> TripAsync(Pegaxus.Repository.AppDbContext db)
    {
        var f = await VehicleIncidentTests.Scenario.CreateAsync(db);
        var nextDriver = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.DRIVER);
        var nextEscort = await PersonnelAssignmentTests.EmployeeAsync(db, EmployeeRole.ESCORT);
        var second = PersonnelAssignmentTests.Segment(f.Segment.RoutePlan, 2);
        second.DriverEmployeeId = nextDriver.EmployeeId; second.EscortEmployeeId = nextEscort.EmployeeId;
        db.Add(second);
        await db.SaveChangesAsync();
        return new(f, second, nextDriver, nextEscort);
    }

    private static async Task HandOverAsync(Pegaxus.Repository.AppDbContext db, Trip trip)
    {
        trip.F.Segment.ActualArrivalAt = trip.Second.ActualDepartureAt = VehicleIncidentTests.Scenario.Now.AddMinutes(-30);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Report_OnlyTheCrewOfTheSegmentInProgress_MayReport()
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        var f = trip.F;

        // Segment 1 is under way: the next crew is assigned to the trip but does not have the horses yet.
        Assert.Equal("NOT_CURRENT_SEGMENT_CREW", (await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Service.ReportVehicleAsync(f.Report(), trip.NextDriver.AccountId))).ErrorCode);
        var first = await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId);

        await HandOverAsync(db, trip);
        var late = f.Report(); late.OccurredAt = late.OccurredAt!.Value.AddSeconds(30);
        Assert.Equal("NOT_CURRENT_SEGMENT_CREW", (await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Service.ReportVehicleAsync(late, f.Driver.AccountId))).ErrorCode);
        // The offline retry of the report made before the handover is still answered.
        Assert.False((await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId)).Created);
        Assert.True((await f.Service.ReportVehicleAsync(late, trip.NextDriver.AccountId)).Created);
        Assert.Equal(2, await db.IncidentReports.CountAsync());
        Assert.NotEqual(Guid.Empty, first.Value.IncidentReportId);
    }

    [Fact]
    public async Task Lifecycle_ThePreviousCrewMayCloseOnlyItsOwnReport_NotTheOneOfTheCrewOnDuty()
    {
        await using var db = TestSupport.CreateDb();
        var trip = await TripAsync(db);
        var f = trip.F;
        var own = await f.Service.ReportVehicleAsync(f.Report(), f.Driver.AccountId);
        await HandOverAsync(db, trip);
        var late = f.Report(); late.OccurredAt = late.OccurredAt!.Value.AddSeconds(30);
        var theirs = await f.Service.ReportVehicleAsync(late, trip.NextDriver.AccountId);

        // The escort of segment 1 neither reported it nor has the horses any more.
        Assert.Equal("NOT_CURRENT_SEGMENT_CREW", (await Assert.ThrowsAsync<ForbiddenException>(() => f.Service.CancelAsync(theirs.Value.IncidentReportId,
            new() { LastUpdatedAt = theirs.Value.UpdatedAt, CancellationReason = "Reported by mistake, all fine" }, f.Escort.AccountId))).ErrorCode);
        var actions = Assert.IsType<Pegaxus.Service.IncidentService.Response.Incident>(await f.Service.GetAsync(theirs.Value.IncidentReportId, f.Escort.AccountId)).AvailableActions;
        Assert.DoesNotContain("CANCEL_INCIDENT", actions); Assert.DoesNotContain("SELF_RESOLVE", actions); Assert.DoesNotContain("REQUEST_COST", actions);

        // The reporter keeps the incident they know best.
        var cancelled = await f.Service.CancelAsync(own.Value.IncidentReportId,
            new() { LastUpdatedAt = own.Value.UpdatedAt, CancellationReason = "Reported by mistake, all fine" }, f.Driver.AccountId);
        Assert.Equal("CANCELLED", cancelled.Status);
        Assert.Contains("CANCEL_INCIDENT", Assert.IsType<Pegaxus.Service.IncidentService.Response.Incident>(await f.Service.GetAsync(theirs.Value.IncidentReportId, trip.NextEscort.AccountId)).AvailableActions);
    }
}
