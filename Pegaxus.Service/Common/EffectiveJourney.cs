using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class EffectiveJourney
{
    public sealed record Segment(Guid RoutePlanRouteId, Guid RoutePlanId, Guid? PrimaryRoutePlanId,
        RoutePlanStatus PlanStatus, DateTime PlanCreatedAt, DateTime PlanUpdatedAt, int SequenceNo,
        RouteType RouteType, DateTime? SupersededAt, DateTime? ActualDepartureAt, DateTime? ActualArrivalAt);

    public static List<Segment> Select(IReadOnlyList<Segment> segments)
    {
        var plans = segments.GroupBy(segment => segment.RoutePlanId).ToList();
        var primary = plans.Where(plan => plan.First().PrimaryRoutePlanId == null && plan.All(segment => segment.RouteType == RouteType.PRIMARY))
            .OrderByDescending(plan => plan.First().PlanCreatedAt).ThenBy(plan => plan.Key).FirstOrDefault();
        if (primary is null) return [];
        var alternatives = plans.Where(plan => plan.First().PrimaryRoutePlanId == primary.Key).ToList();
        var active = alternatives.Where(plan => plan.First().PlanStatus == RoutePlanStatus.ACTIVE ||
                plan.First().PlanStatus == RoutePlanStatus.COMPLETED && plan.Any(segment => segment.ActualDepartureAt.HasValue))
            .OrderByDescending(plan => plan.First().PlanStatus == RoutePlanStatus.ACTIVE)
            .ThenByDescending(plan => plan.First().PlanUpdatedAt).ThenBy(plan => plan.Key).FirstOrDefault();
        if (active is null)
            return primary.Where(segment => segment.SupersededAt == null || segment.ActualDepartureAt.HasValue)
                .OrderBy(segment => segment.SequenceNo).ThenBy(segment => segment.RoutePlanRouteId).ToList();

        // DELIVERY completes the active plan; preserve its executed contingency in the completed journey.
        var history = primary.Concat(alternatives.Where(plan => plan.Key != active.Key).SelectMany(plan => plan))
            .Where(segment => segment.ActualDepartureAt.HasValue).OrderBy(segment => segment.ActualDepartureAt)
            .ThenBy(segment => segment.SequenceNo).ThenBy(segment => segment.RoutePlanRouteId);
        var branch = active.Where(segment => segment.RouteType == RouteType.BACKUP).Select(segment => (int?)segment.SequenceNo).Min();
        return history.Concat(active.Where(segment => (branch == null || segment.SequenceNo >= branch) &&
                (segment.SupersededAt == null || segment.ActualDepartureAt.HasValue))
            .OrderBy(segment => segment.SequenceNo).ThenBy(segment => segment.RoutePlanRouteId)).ToList();
    }

    public static Segment? Current(IReadOnlyList<Segment> segments, TransportStatus status) =>
        // shortcut: infer the current segment from actual times until B-06 adds segment status.
        segments.FirstOrDefault(segment => segment.ActualDepartureAt.HasValue && !segment.ActualArrivalAt.HasValue) ?? status switch
        {
            TransportStatus.READY => segments.FirstOrDefault(),
            TransportStatus.COMPLETED => segments.LastOrDefault(),
            _ => null
        };
}
