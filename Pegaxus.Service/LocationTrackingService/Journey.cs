using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.LocationTrackingService;

public sealed partial class Service
{
    private async Task<IReadOnlyList<SegmentData>> EffectiveSegmentsAsync(Guid transportId, TransportStatus status, CancellationToken cancellationToken)
    {
        var rows = await db.RoutePlanRoutes.AsNoTracking().Where(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
                EligibleStatuses.Contains(segment.RoutePlan.Status))
            .Select(segment => new SegmentData(segment.RoutePlanRouteId, segment.RoutePlanId, segment.RoutePlan.Status, segment.RoutePlan.UpdatedAt,
                segment.SequenceNo, segment.RouteType, segment.SupersededAt, segment.ActualDepartureAt, segment.ActualArrivalAt,
                segment.PlannedArrivalAt, segment.Route.DistanceKm, segment.Route.TransportMode.ToString(), segment.DriverEmployeeId,
                new(segment.Route.FromLocationId, segment.Route.FromLocation.LocationName, segment.Route.FromLocation.LocationType.HasValue ? segment.Route.FromLocation.LocationType.Value.ToString() : null,
                    segment.Route.FromLocation.City, segment.Route.FromLocation.Country, segment.Route.FromLocation.Latitude, segment.Route.FromLocation.Longitude),
                // A segment that stopped early ends where the convoy turned back, not at its route's end.
                segment.EndedAtLocation != null
                    ? new(segment.EndedAtLocation.LocationId, segment.EndedAtLocation.LocationName, segment.EndedAtLocation.LocationType.HasValue ? segment.EndedAtLocation.LocationType.Value.ToString() : null,
                        segment.EndedAtLocation.City, segment.EndedAtLocation.Country, segment.EndedAtLocation.Latitude, segment.EndedAtLocation.Longitude)
                    : new(segment.Route.ToLocationId, segment.Route.ToLocation.LocationName, segment.Route.ToLocation.LocationType.HasValue ? segment.Route.ToLocation.LocationType.Value.ToString() : null,
                        segment.Route.ToLocation.City, segment.Route.ToLocation.Country, segment.Route.ToLocation.Latitude, segment.Route.ToLocation.Longitude)))
            .ToArrayAsync(cancellationToken);
        var plans = rows.GroupBy(segment => segment.PlanId).OrderByDescending(group => group.First().PlanUpdatedAt).ThenBy(group => group.Key).ToArray();
        var alternative = plans.FirstOrDefault(group => group.Any(segment => segment.Type != RouteType.PRIMARY) &&
            (group.First().PlanStatus == RoutePlanStatus.ACTIVE || status == TransportStatus.COMPLETED && group.First().PlanStatus == RoutePlanStatus.COMPLETED));
        var selected = alternative ?? plans.OrderByDescending(group => group.First().PlanStatus == RoutePlanStatus.ACTIVE)
            .FirstOrDefault(group => group.All(segment => segment.Type == RouteType.PRIMARY));
        if (selected is null) return [];
        var branch = alternative?.Where(segment => segment.Type == RouteType.BACKUP && (segment.SupersededAt is null || segment.ActualDepartureAt.HasValue))
            .Select(segment => (int?)segment.SequenceNo).Min() ?? 1;
        var history = rows.Where(segment => segment.PlanId != selected.Key && segment.ActualDepartureAt.HasValue)
            .OrderBy(segment => segment.ActualDepartureAt).ThenBy(segment => segment.PlanId).ThenBy(segment => segment.SequenceNo);
        // A backup that branches further ahead keeps the untravelled primary segments it did not supersede, up to its branch point.
        var primaryPlan = plans.FirstOrDefault(group => group.Key != selected.Key && group.All(item => item.Type == RouteType.PRIMARY) &&
            group.Any(item => item.SupersededAt is not null));
        var kept = alternative is not null && branch > 1 && primaryPlan is not null
            ? primaryPlan.Where(segment => !segment.ActualDepartureAt.HasValue && segment.SupersededAt is null)
                .OrderBy(segment => segment.SequenceNo).ThenBy(segment => segment.Id).ToArray()
            : [];
        return history.Concat(kept).Concat(selected.Where(segment => segment.SequenceNo >= branch && (segment.SupersededAt is null || segment.ActualDepartureAt.HasValue))
            .OrderBy(segment => segment.SequenceNo).ThenBy(segment => segment.Id)).ToArray();
    }

    // shortcut: derive status from actual timestamps until B-06 adds a segment status column.
    private static string SegmentStatus(SegmentData segment) => segment.ActualArrivalAt.HasValue ? "COMPLETED" : segment.ActualDepartureAt.HasValue ? "IN_TRANSIT" : "PLANNED";

    private static SegmentData? CurrentSegment(IReadOnlyList<SegmentData> segments, TransportStatus status) =>
        segments.FirstOrDefault(segment => segment.ActualDepartureAt.HasValue && !segment.ActualArrivalAt.HasValue) ??
        (status == TransportStatus.READY ? segments.FirstOrDefault() : status == TransportStatus.COMPLETED ? segments.LastOrDefault() : null);

    private sealed record SegmentData(Guid Id, Guid PlanId, RoutePlanStatus PlanStatus, DateTime PlanUpdatedAt, int SequenceNo, RouteType Type,
        DateTime? SupersededAt, DateTime? ActualDepartureAt, DateTime? ActualArrivalAt, DateTime? PlannedArrivalAt, decimal? DistanceKm,
        string Mode, Guid? DriverEmployeeId, Response.LocationBrief From, Response.LocationBrief To);
}
