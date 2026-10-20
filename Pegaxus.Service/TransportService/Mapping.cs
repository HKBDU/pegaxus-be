using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.TransportService;

public sealed partial class Service
{
    private async Task<List<SegmentData>> ReadSegmentsAsync(Guid[] transportIds, Actor actor, CancellationToken cancellationToken)
    {
        if (transportIds.Length == 0) return [];
        return await db.RoutePlanRoutes.AsNoTracking()
            .Where(item => transportIds.Contains(item.RoutePlan.TransportPlan.TransportId) && VisiblePlanStatuses.Contains(item.RoutePlan.Status))
            .Select(item => new SegmentData(item.RoutePlan.TransportPlan.TransportId, item.RoutePlan.PrimaryRoutePlanId,
                item.RoutePlan.Status, item.RoutePlan.CreatedAt, item.RoutePlan.UpdatedAt, item.SupersededAt,
                item.DriverEmployeeId, item.EscortEmployeeId, item.Route.FromLocation.Address, item.Route.ToLocation.Address,
                new Response.TrackingSegment
                {
                    RoutePlanRouteId = item.RoutePlanRouteId,
                    RoutePlanId = item.RoutePlanId,
                    RoutePlanCode = item.RoutePlan.RoutePlanCode,
                    SequenceNo = item.SequenceNo,
                    RouteType = item.RouteType.ToString(),
                    TransportMode = item.Route.TransportMode.ToString(),
                    FromLocation = ToLocation(item.Route.FromLocation),
                    ToLocation = ToLocation(item.Route.ToLocation),
                    DistanceKm = item.Route.DistanceKm,
                    // shortcut: derive status from actual times until B-06 adds RoutePlanRoute.status.
                    Status = item.ActualArrivalAt.HasValue ? "COMPLETED" : item.ActualDepartureAt.HasValue ? "IN_TRANSIT" : "PLANNED",
                    PlannedDepartureAt = AsUtc(item.PlannedDepartureAt),
                    PlannedArrivalAt = AsUtc(item.PlannedArrivalAt),
                    ActualDepartureAt = AsUtc(item.ActualDepartureAt),
                    ActualArrivalAt = AsUtc(item.ActualArrivalAt),
                    UpdatedAt = AsUtc(item.UpdatedAt),
                    Driver = item.TransportProviderId.HasValue || item.DriverEmployee == null ? null :
                        new Response.CrewMember(item.DriverEmployee.EmployeeId, item.DriverEmployee.EmployeeCode, item.DriverEmployee.FullName,
                            item.DriverEmployee.Role.ToString(), actor.IsCustomer ? null : item.DriverEmployee.Phone),
                    Escort = item.TransportProviderId.HasValue || item.EscortEmployee == null ? null :
                        new Response.CrewMember(item.EscortEmployee.EmployeeId, item.EscortEmployee.EmployeeCode, item.EscortEmployee.FullName,
                            item.EscortEmployee.Role.ToString(), actor.IsCustomer ? null : item.EscortEmployee.Phone),
                    IsOutsourced = item.TransportProviderId.HasValue,
                    Provider = item.TransportProvider == null ? null : new Response.ProviderBrief(item.TransportProvider.TransportProviderId,
                        item.TransportProvider.ProviderName, item.ServiceType, item.ProviderReference),
                    Vehicles = item.RoutePlanRouteVehicles.Where(vehicle => !actor.IsCustomer && !item.TransportProviderId.HasValue)
                        .OrderBy(vehicle => vehicle.VehicleId).Select(vehicle => new Response.VehicleBrief(vehicle.VehicleId,
                            vehicle.Vehicle.VehicleCode, vehicle.Vehicle.LicensePlate)).ToList(),
                    Note = actor.IsCustomer ? null : item.Note
                })).AsSplitQuery().ToListAsync(cancellationToken);
    }

    private static List<SegmentData> EffectiveSegments(List<SegmentData> segments)
    {
        var plans = segments.GroupBy(item => item.Segment.RoutePlanId).ToList();
        var primary = plans.Where(plan => plan.First().PrimaryRoutePlanId == null && plan.All(item => item.Segment.RouteType == "PRIMARY"))
            .OrderByDescending(plan => plan.First().PlanCreatedAt).ThenBy(plan => plan.Key).FirstOrDefault();
        if (primary is null) return [];
        var alternatives = plans.Where(plan => plan.First().PrimaryRoutePlanId == primary.Key).ToList();
        var active = alternatives.Where(plan => plan.First().PlanStatus == RoutePlanStatus.ACTIVE ||
                plan.First().PlanStatus == RoutePlanStatus.COMPLETED && plan.Any(item => item.Segment.ActualDepartureAt.HasValue))
            .OrderByDescending(plan => plan.First().PlanUpdatedAt).ThenBy(plan => plan.Key).FirstOrDefault();
        if (active is null)
            return primary.Where(item => item.SupersededAt == null || item.Segment.ActualDepartureAt.HasValue)
                .OrderBy(item => item.Segment.SequenceNo).ToList();

        // DELIVERY completes the active plan; keep its executed contingency in the completed journey.
        var history = primary.Concat(alternatives.Where(plan => plan.Key != active.Key).SelectMany(plan => plan))
            .Where(item => item.Segment.ActualDepartureAt.HasValue).OrderBy(item => item.Segment.ActualDepartureAt)
            .ThenBy(item => item.Segment.SequenceNo).ThenBy(item => item.Segment.RoutePlanRouteId);
        var branch = active.Where(item => item.Segment.RouteType == "BACKUP").Select(item => (int?)item.Segment.SequenceNo).Min();
        return history.Concat(active.Where(item => (branch == null || item.Segment.SequenceNo >= branch) &&
            (item.SupersededAt == null || item.Segment.ActualDepartureAt.HasValue)).OrderBy(item => item.Segment.SequenceNo)).ToList();
    }

    private static SegmentData? CurrentSegment(List<SegmentData> segments, TransportStatus status) =>
        segments.FirstOrDefault(item => item.Segment.Status == "IN_TRANSIT") ?? status switch
        {
            TransportStatus.READY => segments.FirstOrDefault(),
            TransportStatus.COMPLETED => segments.LastOrDefault(),
            _ => null
        };

    private static Response.SegmentBrief? ToBrief(SegmentData? item, List<SegmentData> segments) => item is null ? null :
        new(item.Segment.RoutePlanRouteId, segments.IndexOf(item) + 1, item.Segment.FromLocation.LocationName,
            item.Segment.ToLocation.LocationName, item.Segment.Status);

    private static List<Response.TrackingSegment> ToTrackingSegments(List<SegmentData> segments, TransportStatus status)
    {
        var current = CurrentSegment(segments, status);
        return segments.Select((item, index) => item.Segment with
        {
            DisplayOrder = index + 1,
            IsCurrent = item == current
        }).ToList();
    }

    private static Response.LocationBrief ToLocation(Location item) => new(item.LocationId, item.LocationName,
        item.LocationType.HasValue ? item.LocationType.Value.ToString() : null, item.City, item.Country, item.Latitude, item.Longitude);

    private async Task<List<Response.HandoverBoundary>> ReadBoundariesAsync(TransportData transport, List<SegmentData> segments,
        Actor actor, CancellationToken cancellationToken)
    {
        if (segments.Count == 0) return [];
        var handovers = await db.HandoverRecords.AsNoTracking().Where(item => item.TransportId == transport.TransportId &&
                item.Status != HandoverStatus.CANCELLED && (!actor.IsCustomer || item.HandoverType == HandoverType.PICKUP || item.HandoverType == HandoverType.DELIVERY))
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.SequenceNo)
            .Select(item => new
            {
                item.FromRoutePlanRouteId,
                item.ToRoutePlanRouteId,
                Brief = new Response.HandoverBrief(item.HandoverRecordId, item.HandoverCode, item.HandoverType.ToString(), item.Status.ToString())
            })
            .ToListAsync(cancellationToken);
        var boundaries = new List<Response.HandoverBoundary>();
        for (var index = 0; index <= segments.Count; index++)
        {
            var from = index > 0 ? segments[index - 1] : null;
            var to = index < segments.Count ? segments[index] : null;
            var anchor = index == 0 ? segments[0] : segments[index - 1];
            var location = index == 0 ? anchor.Segment.FromLocation : anchor.Segment.ToLocation;
            var address = index == 0 ? anchor.FromAddress : anchor.ToAddress;
            var existing = handovers.FirstOrDefault(item => item.FromRoutePlanRouteId == from?.Segment.RoutePlanRouteId &&
                item.ToRoutePlanRouteId == to?.Segment.RoutePlanRouteId)?.Brief;
            var outsourced = from?.Segment.IsOutsourced == true || to?.Segment.IsOutsourced == true;
            var required = from is null || to is null || outsourced || HandoverRules.RequiredBetween(from.DriverEmployeeId, to.DriverEmployeeId);
            // shortcut: provider handovers stay disabled until B-03 adds PROVIDER parties.
            string[] allowed = outsourced || !required ? [] : index == 0 ? ["PICKUP"] : index == segments.Count ? ["DELIVERY"] : ["TRANSFER", "BORDER_HANDOVER"];
            var open = !outsourced && required && existing is null && (index == 0 ? transport.Status == TransportStatus.READY : transport.Status == TransportStatus.IN_TRANSIT);
            var isManager = actor.Role is EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR;
            // Crew start a handover on the spot; managers and specialists may also schedule one, but specialists cannot start it.
            var modes = new List<string>();
            if (open && (isManager || IsAssigned(from, actor) || IsAssigned(to, actor))) modes.Add("START_NOW");
            if (open && (isManager || actor.Role == EmployeeRole.TRANSPORT_SPECIALIST)) modes.Add("SCHEDULE");
            boundaries.Add(new(index + 1, from?.Segment.RoutePlanRouteId, to?.Segment.RoutePlanRouteId,
                new(location.LocationName, address, location.Country), required, from?.Segment.ActualArrivalAt, allowed, existing, modes.Count > 0, modes));
        }
        return boundaries;
    }

    private static bool IsAssigned(SegmentData? item, Actor actor) => item is not null && actor.IsCrew &&
        (actor.Role == EmployeeRole.DRIVER && item.DriverEmployeeId == actor.EmployeeId ||
         actor.Role == EmployeeRole.ESCORT && item.EscortEmployeeId == actor.EmployeeId);

    private sealed record SegmentData(Guid TransportId, Guid? PrimaryRoutePlanId, RoutePlanStatus PlanStatus,
        DateTime PlanCreatedAt, DateTime PlanUpdatedAt, DateTime? SupersededAt, Guid? DriverEmployeeId, Guid? EscortEmployeeId,
        string? FromAddress, string? ToAddress, Response.TrackingSegment Segment);
}
