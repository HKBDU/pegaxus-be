using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

public static class TransportItinerary
{
    public enum BoundaryPosition { Start, Middle, End }

    public sealed record Segment(RoutePlanRoute Route, int DisplayOrder, RouteSegmentStatus Status);

    public sealed record Boundary(
        int Index, BoundaryPosition Position, HandoverType Type, Segment? From, Segment? To, HandoverRecord? ExistingHandover)
    {
        public bool InvolvesOutsourcedSegment => IsOutsourced(From) || IsOutsourced(To);

        private static bool IsOutsourced(Segment? segment) => segment?.Route.TransportProviderId.HasValue == true;
    }

    public static RouteSegmentStatus StatusOf(RoutePlanRoute route) =>
        route.ActualArrivalAt.HasValue ? RouteSegmentStatus.COMPLETED
        : route.ActualDepartureAt.HasValue ? RouteSegmentStatus.IN_TRANSIT
        : RouteSegmentStatus.PLANNED;

    public static bool IsPrimaryPlan(RoutePlan plan) =>
        plan.RoutePlanRoutes.Count > 0 && plan.RoutePlanRoutes.All(route => route.RouteType == RouteType.PRIMARY);

    public static bool IsBackupPlan(RoutePlan plan) => plan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.BACKUP);

    public static bool IsReturnPlan(RoutePlan plan) =>
        !IsBackupPlan(plan) && plan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.RETURN);

    /// <summary>BACKUP if any segment is BACKUP, RETURN if the plan is a return plan, otherwise PRIMARY.</summary>
    public static RouteType PlanType(RoutePlan plan) =>
        IsBackupPlan(plan) ? RouteType.BACKUP : IsReturnPlan(plan) ? RouteType.RETURN : RouteType.PRIMARY;

    /// <summary>Sequence number of the first BACKUP segment of a backup plan; its departure location is the branch point.</summary>
    public static int? BranchSequenceNo(RoutePlan plan)
    {
        var backups = plan.RoutePlanRoutes.Where(route => route.RouteType == RouteType.BACKUP).ToList();
        return backups.Count == 0 ? null : backups.Min(route => route.SequenceNo);
    }

    public static RoutePlanRoute? BranchSegment(RoutePlan plan) =>
        BranchSequenceNo(plan) is { } sequenceNo ? plan.RoutePlanRoutes.First(route => route.SequenceNo == sequenceNo) : null;

    /// <summary>
    /// Effective itinerary of a transport: the PRIMARY plan's segments that are not superseded or already departed.
    /// An ACTIVE or COMPLETED BACKUP plan keeps the departed primary segments and continues with its own segments from
    /// the first BACKUP segment onwards. An ACTIVE or COMPLETED RETURN plan keeps every departed segment and appends
    /// all of its own. Plans that are not yet active never contribute.
    /// </summary>
    public static IReadOnlyList<Segment> Effective(IEnumerable<RoutePlan> plans)
    {
        var all = plans.ToList();
        var routes = Pick(all.Where(IsPrimaryPlan), RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED, RoutePlanStatus.CONFIRMED) is { } primary
            ? Visible(primary).ToList()
            : [];

        if (Pick(all.Where(IsBackupPlan), RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED) is { } backup &&
            BranchSequenceNo(backup) is { } branch)
            routes = Departed(routes).Concat(Visible(backup).Where(route => route.SequenceNo >= branch)).ToList();

        if (Pick(all.Where(IsReturnPlan), RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED) is { } returning)
            routes = Departed(routes).Concat(Visible(returning)).ToList();

        return routes.Select((route, index) => new Segment(route, index + 1, StatusOf(route))).ToList();
    }

    public static Segment? Current(IReadOnlyList<Segment> segments, TransportStatus transportStatus) =>
        segments.FirstOrDefault(segment => segment.Status == RouteSegmentStatus.IN_TRANSIT)
        ?? transportStatus switch
        {
            TransportStatus.READY => segments.FirstOrDefault(),
            TransportStatus.COMPLETED => segments.LastOrDefault(),
            _ => null
        };

    public static DateTime? DepartureAt(Transport transport, IReadOnlyList<Segment> segments) =>
        transport.ActualDepartureAt ?? segments.FirstOrDefault()?.Route.PlannedDepartureAt;

    public static DateTime? EtaAt(IReadOnlyList<Segment> segments) => segments.LastOrDefault()?.Route.PlannedArrivalAt;

    /// <summary>n segments give n + 1 boundaries: one PICKUP, n - 1 TRANSFER and one DELIVERY.</summary>
    public static IReadOnlyList<Boundary> Boundaries(IReadOnlyList<Segment> segments, IEnumerable<HandoverRecord> handovers)
    {
        if (segments.Count == 0) return [];
        var records = handovers.Where(record => record.Status != HandoverStatus.CANCELLED)
            .OrderByDescending(record => record.SequenceNo).ToList();
        var boundaries = new List<Boundary>(segments.Count + 1);
        for (var index = 0; index <= segments.Count; index++)
        {
            var from = index == 0 ? null : segments[index - 1];
            var to = index == segments.Count ? null : segments[index];
            var position = index == 0 ? BoundaryPosition.Start : index == segments.Count ? BoundaryPosition.End : BoundaryPosition.Middle;
            var type = position switch
            {
                BoundaryPosition.Start => HandoverType.PICKUP,
                BoundaryPosition.End => HandoverType.DELIVERY,
                _ => HandoverType.TRANSFER
            };
            var existing = records.FirstOrDefault(record =>
                record.FromRoutePlanRouteId == from?.Route.RoutePlanRouteId && record.ToRoutePlanRouteId == to?.Route.RoutePlanRouteId);
            boundaries.Add(new(index, position, type, from, to, existing));
        }
        return boundaries;
    }

    public static bool CanCreate(Boundary boundary, TransportStatus transportStatus, bool callerMayCreate) =>
        callerMayCreate && boundary.ExistingHandover is null &&
        transportStatus == (boundary.Position == BoundaryPosition.Start ? TransportStatus.READY : TransportStatus.IN_TRANSIT);

    private static bool IsVisible(RoutePlanRoute route) => route.SupersededAt is null || route.ActualDepartureAt.HasValue;

    private static IEnumerable<RoutePlanRoute> Visible(RoutePlan plan) =>
        plan.RoutePlanRoutes.Where(IsVisible).OrderBy(route => route.SequenceNo);

    private static IEnumerable<RoutePlanRoute> Departed(IEnumerable<RoutePlanRoute> routes) =>
        routes.Where(route => route.ActualDepartureAt.HasValue);

    private static RoutePlan? Pick(IEnumerable<RoutePlan> plans, params RoutePlanStatus[] statusPriority) =>
        plans.Where(plan => statusPriority.Contains(plan.Status))
            .OrderBy(plan => Array.IndexOf(statusPriority, plan.Status))
            .ThenByDescending(plan => plan.CreatedAt)
            .FirstOrDefault();
}
