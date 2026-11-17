using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.RouteChangeService;

/// <summary>
/// Where a backup or return route meets the running journey. From PICKUP to DELIVERY one segment is always running, because
/// reaching a stop records the arrival of a segment and the departure of the next at once. So the new route starts either
/// further ahead (the running segment goes on), or behind / at the incident (the running segment stops early there).
/// </summary>
public sealed partial class Service
{
    /// <summary>The convoy turns back at the incident; the running segment ends there.</summary>
    internal const string IncidentLocation = "INCIDENT_LOCATION";
    /// <summary>The convoy goes on to a stop ahead (the end of the running segment, or a later one) and takes the new route there.</summary>
    internal const string NextStop = "NEXT_STOP";
    /// <summary>Nothing is running: the convoy waits at a stop and takes the new route from there.</summary>
    internal const string AtStop = "AT_STOP";

    /// <param name="Mode">INCIDENT_LOCATION, NEXT_STOP or AT_STOP.</param>
    /// <param name="Start">Where the new route starts (the branch point of a backup, the first stop of a return).</param>
    /// <param name="TurnsBack">The convoy drives back to <paramref name="Start"/>, which it has already passed.</param>
    /// <param name="Current">The running segment, if any.</param>
    /// <param name="Replaced">Untravelled segments of the running journey that the new route replaces.</param>
    /// <param name="Tail">The segments of the new route that take over.</param>
    /// <param name="Skipped">Backup segments before the chosen branch point; the convoy already covered that stretch.</param>
    /// <param name="EndedAt">The incident where the running segment stops early; null when it goes on.</param>
    /// <param name="Journey">The running journey the new route joins.</param>
    private sealed record Anchor(string Mode, Response.LocationBrief Start, bool TurnsBack, Response.Segment? Current,
        List<Response.Segment> Replaced, List<Response.Segment> Tail, List<Response.Segment> Skipped, IncidentReport? EndedAt,
        List<Response.Segment> Journey);

    private static Response.Segment? Running(List<Response.Segment> journey) =>
        journey.FirstOrDefault(x => x.ActualDepartureAt.HasValue && !x.ActualArrivalAt.HasValue);

    /// <summary>
    /// A backup may branch off the primary at several stops (it can leave, rejoin and leave again). The nearest branch point
    /// ahead of the convoy wins, so it never turns back when it does not have to. When every branch point is behind, the convoy
    /// turns back to the nearest one and the running segment stops at the incident.
    /// </summary>
    private async Task<Anchor> BackupAnchorAsync(Guid transportId, List<Response.Segment> backup, List<Response.Segment> primary,
        Guid? incidentReportId, CancellationToken token)
    {
        var current = Running(primary);
        var untravelled = primary.Where(x => !x.ActualDepartureAt.HasValue).ToList();
        if (current is null && untravelled.Count == 0)
            throw new ConflictException("Only untravelled segments can be replaced.", "SEGMENT_LOCKED");
        var firstBackup = backup.FindIndex(x => x.RouteType == "BACKUP");
        if (firstBackup < 0) throw new BadRequestException("Backup itinerary is empty.");
        var stops = primary.Select(x => x.FromLocation.LocationId).ToHashSet();
        var branches = Enumerable.Range(firstBackup, backup.Count - firstBackup)
            .Where(i => backup[i].RouteType == "BACKUP" && stops.Contains(backup[i].FromLocation.LocationId)).ToList();
        if (branches.Count == 0)
            throw new UnprocessableEntityException("The branch point of this backup route is not on the primary route.", "BRANCH_POINT_NOT_ON_ROUTE");
        int StartOf(int branch, bool departed) => primary.FindIndex(x => x.ActualDepartureAt.HasValue == departed &&
            x.FromLocation.LocationId == backup[branch].FromLocation.LocationId);
        Anchor Build(int branch, string mode, bool turnsBack, List<Response.Segment> replaced, IncidentReport? endedAt) =>
            new(mode, backup[branch].FromLocation, turnsBack, current, replaced, backup.Skip(branch).ToList(),
                backup.Skip(firstBackup).Take(branch - firstBackup).ToList(), endedAt, primary);

        var ahead = branches.Select(i => (Branch: i, Stop: StartOf(i, false))).Where(x => x.Stop >= 0).OrderBy(x => x.Stop).ToList();
        if (ahead.Count > 0)
            return Build(ahead[0].Branch,
                current is null && primary[ahead[0].Stop].RoutePlanRouteId == untravelled[0].RoutePlanRouteId ? AtStop : NextStop, false,
                primary.Skip(ahead[0].Stop).ToList(), null);
        var behind = branches.Select(i => (Branch: i, Stop: StartOf(i, true))).Where(x => x.Stop >= 0).OrderByDescending(x => x.Stop).First();
        // Nothing is running only while the convoy waits at a stop: it drives back without a segment to close.
        if (current is null) return Build(behind.Branch, AtStop, true, untravelled, null);
        return Build(behind.Branch, IncidentLocation, true, untravelled, await OpenIncidentAsync(transportId, incidentReportId, null, token)
            ?? throw new UnprocessableEntityException(
                "The convoy has passed every branch point of this backup. Link the open incident so the running segment can stop where it happened.",
                "INCIDENT_LOCATION_REQUIRED"));
    }

    /// <summary>
    /// A return starts at the incident on the running segment (that segment stops there), at the end of the running segment
    /// (it goes on, then the convoy turns back), or at the stop where the convoy waits. It must end at the origin.
    /// </summary>
    private async Task<Anchor> ReturnAnchorAsync(RoutePlan plan, List<Response.Segment> segments, CancellationToken token)
    {
        var active = await EffectiveSegmentsAsync(plan, token);
        var current = Running(active);
        var untravelled = active.Where(x => !x.ActualDepartureAt.HasValue).ToList();
        var start = segments[0].FromLocation;
        if (active.Count == 0 || segments[^1].ToLocation.LocationId != active[0].FromLocation.LocationId)
            throw NotAnchored();
        if (current is not null && start.LocationId == current.ToLocation.LocationId)
            return new(NextStop, start, false, current, untravelled, segments, [], null, active);
        if (current is not null && await OpenIncidentAsync(plan.TransportPlan.TransportId, null, start.LocationId, token) is { } incident)
            return new(IncidentLocation, start, false, current, untravelled, segments, [], incident, active);
        if (current is null && untravelled.Count > 0 && start.LocationId == untravelled[0].FromLocation.LocationId)
            return new(AtStop, start, false, null, untravelled, segments, [], null, active);
        throw NotAnchored();
    }

    private static UnprocessableEntityException NotAnchored() => new(
        "A return must start at an open incident on the running segment, at the end of the running segment, or at the stop where the convoy waits, and end at the origin.",
        "RETURN_NOT_ANCHORED");

    /// <summary>The given open incident of the transport, or else the latest open one (at <paramref name="locationId"/> when set).</summary>
    private Task<IncidentReport?> OpenIncidentAsync(Guid transportId, Guid? incidentReportId, Guid? locationId, CancellationToken token) =>
        db.IncidentReports.AsNoTracking()
            .Where(x => x.TransportId == transportId && (x.Status == IncidentStatus.REPORTED || x.Status == IncidentStatus.PENDING) &&
                (incidentReportId == null || x.IncidentReportId == incidentReportId) && (locationId == null || x.LocationId == locationId))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.IncidentReportId).FirstOrDefaultAsync(token);

    /// <summary>A start time the anchor allows: never before the running segment reaches the stop the new route leaves from.</summary>
    private static void CheckStart(Anchor anchor, List<Response.Segment> tail)
    {
        if (anchor.Current is { } current && !anchor.TurnsBack && anchor.Mode == NextStop &&
            anchor.Start.LocationId == current.ToLocation.LocationId && tail[0].PlannedDepartureAt < current.PlannedArrivalAt)
            throw new UnprocessableEntityException("The new route cannot start before the running segment reaches its end.", "INVALID_START_TIME");
    }

    /// <summary>
    /// Supersedes what the new route replaces, and when the convoy turns back at the incident closes the running segment there.
    /// The first new segment then starts at once unless a driver change needs a handover first.
    /// </summary>
    private async Task ApplyAnchorAsync(Anchor anchor, List<Response.Segment> tail, DateTime now, CancellationToken token)
    {
        await SupersedeAsync(anchor.Replaced.Concat(anchor.Skipped).ToList(), now, token);
        if (anchor.EndedAt is null || anchor.Current is null) return;
        var current = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == anchor.Current.RoutePlanRouteId, token);
        current.ActualArrivalAt = now; current.EndedAtLocationId = anchor.EndedAt.LocationId; current.UpdatedAt = now;
        if (HandoverRules.RequiredBetween(current.DriverEmployeeId, tail[0].Driver?.EmployeeId)) return;
        var first = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == tail[0].RoutePlanRouteId, token);
        first.ActualDepartureAt = now; first.UpdatedAt = now;
    }
}
