using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RouteChangeService;

/// <summary>
/// A backup or return route has no crew of its own. The manager chooses it when the route takes over: the suggestion takes the crew
/// of the replaced primary segments (backup) or the crew at the current stop (return), and the manager fills the gaps, replaces
/// people or vehicles, and sets when the route really starts.
/// </summary>
public sealed partial class Service
{
    /// <summary>A start time this far in the past is still taken, to absorb the time the manager spends on the form.</summary>
    private static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(5);

    public async Task<Response.CrewPlan> GetCrewPlanAsync(Guid routePlanId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        var plan = await db.RoutePlans.AsNoTracking().Include(x => x.TransportPlan).ThenInclude(x => x.Transport)
            .SingleOrDefaultAsync(x => x.RoutePlanId == routePlanId, cancellationToken)
            ?? throw new NotFoundException("Route plan not found.", "ROUTE_PLAN_NOT_FOUND");
        if (actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) await EnsureCoordinatorAsync(plan, actor, cancellationToken);
        EnsureInTransit(plan);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        EnsureTypeStatus(plan, segments, "BACKUP", RoutePlanStatus.CONFIRMED);
        var primary = await SegmentsAsync(await PrimaryIdAsync(plan, cancellationToken), cancellationToken);
        var anchor = await BackupAnchorAsync(plan.TransportPlan.TransportId, segments, primary, null, cancellationToken);
        return await CrewPlanOfAsync(plan, anchor, SuggestBackupCrew(anchor), cancellationToken);
    }

    public async Task<Response.CrewPlan> GetReturnCrewPlanAsync(Guid routePlanId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER], cancellationToken);
        var plan = await db.RoutePlans.AsNoTracking().Include(x => x.TransportPlan).ThenInclude(x => x.Transport)
            .SingleOrDefaultAsync(x => x.RoutePlanId == routePlanId, cancellationToken)
            ?? throw new NotFoundException("Route plan not found.", "ROUTE_PLAN_NOT_FOUND");
        EnsureInTransit(plan);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        EnsureTypeStatus(plan, segments, "RETURN", RoutePlanStatus.CONFIRMED);
        var anchor = await ReturnAnchorAsync(plan, segments, cancellationToken);
        return await CrewPlanOfAsync(plan, anchor, SuggestReturnCrew(anchor), cancellationToken);
    }

    private async Task<Response.CrewPlan> CrewPlanOfAsync(RoutePlan plan, Anchor anchor,
        List<(Response.Segment Segment, Guid? SourceId)> suggested, CancellationToken token)
    {
        // The suggested start is the planned departure, never in the past, and never before the running segment reaches the start.
        var start = new[] { suggested[0].Segment.PlannedDepartureAt, AsUtc(Now),
            anchor.Current is { } current && !anchor.TurnsBack && anchor.Start.LocationId == current.ToLocation.LocationId
                ? current.PlannedArrivalAt : DateTimeOffset.MinValue }.Max();
        var endedAt = anchor.EndedAt is { } incident
            ? Location(await db.Locations.AsNoTracking().SingleAsync(x => x.LocationId == incident.LocationId, token)) : null;
        return new(plan.RoutePlanId, AsUtc(plan.UpdatedAt), start, suggested.Select(x => new Response.CrewSegment(x.Segment, x.SourceId,
            x.Segment.ExecutionType == "INTERNAL" && (x.Segment.Driver is null || x.Segment.Escort is null))).ToList(),
            anchor.Mode, anchor.Start, anchor.TurnsBack, endedAt);
    }

    /// <summary>
    /// The crew with the horse drives on when the convoy turns back; otherwise the crew of the replaced primary segments, by position.
    /// </summary>
    private static List<(Response.Segment Segment, Guid? SourceId)> SuggestBackupCrew(Anchor anchor) =>
        SuggestCrew(anchor.Tail, anchor.TurnsBack && anchor.Current is { } current ? [current, .. anchor.Replaced] : anchor.Replaced);

    /// <summary>
    /// Every internal return segment gets the crew with the horse: the crew of the running segment, or of the next segment while
    /// the convoy waits at a stop, or else the crew that brought the horse there.
    /// </summary>
    private static List<(Response.Segment Segment, Guid? SourceId)> SuggestReturnCrew(Anchor anchor)
    {
        static bool Crewed(Response.Segment x) => x.Driver is not null && x.Escort is not null;
        var near = anchor.Current ?? anchor.Replaced.FirstOrDefault();
        var source = near is not null && Crewed(near) ? near : anchor.Journey.LastOrDefault(x => x.ActualArrivalAt.HasValue && Crewed(x));
        return source is null
            ? anchor.Tail.Select(x => (x with { Driver = null, Escort = null }, (Guid?)null)).ToList()
            : SuggestCrew(anchor.Tail, [source]);
    }

    /// <summary>Applies the manager's picks over the suggestion; an empty field keeps the suggested person.</summary>
    private async Task<List<Response.Segment>> ApplyCrewAsync(List<Response.Segment> tail, List<Request.CrewRequest>? changes, CancellationToken token)
    {
        var result = tail.ToList();
        foreach (var change in changes ?? [])
        {
            var index = result.FindIndex(x => x.RoutePlanRouteId == change.RoutePlanRouteId);
            if (index < 0) throw new NotFoundException("Segment not found in the part of the route that takes over.", "SEGMENT_NOT_FOUND");
            var segment = result[index];
            if (segment.ExecutionType != "INTERNAL")
            {
                if (change.DriverEmployeeId.HasValue || change.EscortEmployeeId.HasValue || change.VehicleIds is { Count: > 0 })
                    throw new UnprocessableEntityException("Outsourced segments cannot use internal vehicles or personnel.", "OUTSOURCED_SEGMENT");
                continue;
            }
            if (change.DriverEmployeeId is { } driverId) segment = segment with { Driver = await EmployeeBriefAsync(driverId, token) };
            if (change.EscortEmployeeId is { } escortId) segment = segment with { Escort = await EmployeeBriefAsync(escortId, token) };
            if (change.VehicleIds is { } ids)
            {
                var vehicles = await db.Vehicles.AsNoTracking().Where(x => ids.Contains(x.VehicleId)).OrderBy(x => x.VehicleId).ToListAsync(token);
                if (vehicles.Count != ids.Count) throw new NotFoundException("Vehicle not found", "VEHICLE_NOT_FOUND");
                segment = segment with { Vehicles = vehicles.Select(Vehicle).ToList() };
            }
            result[index] = segment;
        }
        return result;
    }

    private async Task<Response.EmployeeBrief> EmployeeBriefAsync(Guid employeeId, CancellationToken token) =>
        Employee(await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == employeeId, token))
            ?? throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");

    /// <summary>
    /// Moves every backup segment by the same amount so the first one departs at <paramref name="startAt"/>. Only the segment
    /// times change: the transport plan's committed end stays, so the on-time report still counts the delay.
    /// </summary>
    private List<Response.Segment> Shift(List<Response.Segment> tail, DateTimeOffset startAt)
    {
        var start = AsUtc(Truncate(startAt));
        if (start < AsUtc(Now) - StartGrace)
            throw new UnprocessableEntityException("The backup route cannot start in the past.", "INVALID_START_TIME");
        var delta = start - tail[0].PlannedDepartureAt;
        return tail.Select(x => x with { PlannedDepartureAt = x.PlannedDepartureAt + delta, PlannedArrivalAt = x.PlannedArrivalAt + delta }).ToList();
    }

    /// <summary>Writes the crew, vehicles, provider and times of the segments, and moves vehicles between the pool and the plan.</summary>
    private async Task WriteResourcesAsync(List<Response.Segment> proposed, RoutePlan plan, DateTime now, CancellationToken token)
    {
        var removedVehicleIds = new List<Guid>();
        foreach (var segment in proposed)
        {
            var row = await db.RoutePlanRoutes.Include(x => x.RoutePlanRouteVehicles).SingleAsync(x => x.RoutePlanRouteId == segment.RoutePlanRouteId, token);
            row.TransportProviderId = segment.Outsourcing?.Provider.TransportProviderId;
            row.DriverEmployeeId = segment.Driver?.EmployeeId; row.EscortEmployeeId = segment.Escort?.EmployeeId;
            row.PlannedDepartureAt = Truncate(segment.PlannedDepartureAt); row.PlannedArrivalAt = Truncate(segment.PlannedArrivalAt);
            row.Note = segment.Note; row.UpdatedAt = now;
            var desired = segment.Vehicles.Select(x => x.VehicleId).ToArray();
            var dropped = row.RoutePlanRouteVehicles.Where(x => !desired.Contains(x.VehicleId)).ToList();
            removedVehicleIds.AddRange(dropped.Select(x => x.VehicleId));
            db.RoutePlanRouteVehicles.RemoveRange(dropped);
            foreach (var id in desired.Where(id => !row.RoutePlanRouteVehicles.Any(x => x.VehicleId == id)))
                db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleId = id, AssignedAt = now });
            if (row.TransportProviderId is Guid providerId && !await db.TransportPlanProviders.AnyAsync(x =>
                    x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId, token) &&
                !db.TransportPlanProviders.Local.Any(x => x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId))
                db.TransportPlanProviders.Add(new() { TransportPlanId = plan.TransportPlanId, TransportProviderId = providerId });
        }
        foreach (var id in proposed.SelectMany(x => x.Vehicles).Select(x => x.VehicleId).Distinct())
        {
            var vehicle = await db.Vehicles.SingleAsync(x => x.VehicleId == id, token);
            if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(token);
            if (vehicle.Status == VehicleStatus.AVAILABLE) { vehicle.Status = VehicleStatus.ASSIGNED; vehicle.UpdatedAt = now; }
        }
        // A dropped vehicle goes back to the pool unless this plan still uses it or another confirmed or active plan holds it.
        var kept = proposed.SelectMany(x => x.Vehicles).Select(x => x.VehicleId).ToHashSet();
        var droppedIds = removedVehicleIds.Where(id => !kept.Contains(id)).Distinct().ToList();
        var heldElsewhere = await db.RoutePlanRouteVehicles.AsNoTracking()
            .Where(x => droppedIds.Contains(x.VehicleId) && x.RoutePlanRoute.RoutePlanId != plan.RoutePlanId && x.RoutePlanRoute.ActualArrivalAt == null &&
                (x.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.CONFIRMED || x.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.ACTIVE))
            .Select(x => x.VehicleId).Distinct().ToListAsync(token);
        foreach (var id in droppedIds.Except(heldElsewhere))
        {
            var vehicle = await db.Vehicles.SingleAsync(x => x.VehicleId == id, token);
            if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(token);
            if (vehicle.Status == VehicleStatus.ASSIGNED) { vehicle.Status = VehicleStatus.AVAILABLE; vehicle.UpdatedAt = now; }
        }
    }

    /// <summary>
    /// Tells each crew member what the change means for them: the new crew gets its segments, the crew of the segment on the way
    /// hands over at the branch point, and the crew whose primary segments were replaced learns it has no further segment.
    /// <paramref name="kind"/> is "backup" or "return". Returns the drivers who still drive, for the mail.
    /// </summary>
    private async Task<List<Employee>> NotifyCrewChangeAsync(RoutePlan plan, string kind, string title, Anchor anchor,
        List<Response.Segment> tail, string? note, DateTime now, CancellationToken token)
    {
        var active = anchor.Journey;
        var range = anchor.Replaced;
        var assigned = tail.SelectMany(segment => new[] { segment.Driver, segment.Escort }.OfType<Response.EmployeeBrief>()
                .Select(person => (person.EmployeeId, Segment: segment)))
            .GroupBy(x => x.EmployeeId).ToDictionary(group => group.Key, group => group.Select(x => x.Segment).Distinct().ToList());
        var onTheWay = active.Where(x => x.ActualDepartureAt.HasValue && !x.ActualArrivalAt.HasValue)
            .SelectMany(x => new[] { x.Driver?.EmployeeId, x.Escort?.EmployeeId }).OfType<Guid>().ToHashSet();
        var released = range.SelectMany(x => new[] { x.Driver?.EmployeeId, x.Escort?.EmployeeId }).OfType<Guid>()
            .Where(id => !assigned.ContainsKey(id) && !onTheWay.Contains(id)).ToHashSet();
        var ids = assigned.Keys.Concat(onTheWay).Concat(released).Distinct().ToArray();
        var people = await db.Employees.AsNoTracking().Active().Where(x => ids.Contains(x.EmployeeId))
            .OrderBy(x => x.EmployeeCode).ToListAsync(token);
        foreach (var person in people)
        {
            // The crew on the way first learns what to do with the running segment.
            var onTheWayStep = onTheWay.Contains(person.EmployeeId)
                ? anchor.EndedAt is not null
                    ? anchor.TurnsBack
                        ? $"Stop where the incident happened and turn back to {anchor.Start.LocationName}. "
                        : $"Stop where the incident happened: the convoy follows {kind} route plan {plan.RoutePlanCode} from there. "
                    : $"Finish your current segment, then the convoy follows {kind} route plan {plan.RoutePlanCode} from {anchor.Start.LocationName}. "
                : "";
            var message = onTheWayStep + (assigned.TryGetValue(person.EmployeeId, out var mine)
                ? $"You now run {kind} route plan {plan.RoutePlanCode}: {string.Join("; ", mine.Select(Describe))}."
                : released.Contains(person.EmployeeId)
                    ? $"Your remaining segments on this transport were replaced by {kind} route plan {plan.RoutePlanCode}. You have no further segment on it."
                    : "Hand over the horses there to the crew of the new route.");
            Notify(person.AccountId, plan, title, message + NoteSuffix(note), NotificationPriority.URGENT, now, null);
        }
        return people.Where(x => x.Role == EmployeeRole.DRIVER && !released.Contains(x.EmployeeId)).ToList();
    }

    private static string Describe(Response.Segment segment) => FormattableString.Invariant(
        $"{segment.FromLocation.LocationName} to {segment.ToLocation.LocationName} departing {segment.PlannedDepartureAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
}
