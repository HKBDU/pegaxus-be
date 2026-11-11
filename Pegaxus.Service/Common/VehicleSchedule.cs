using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.Common;

/// <summary>
/// One rule for "does this trip still hold its vehicle": the vehicle picker and the route plan save check both use it,
/// so a vehicle the picker lists as free is never rejected on save. A finished trip (arrived segment, or a completed or
/// cancelled plan or transport) no longer holds its vehicle, even if its planned times are still in the future.
/// </summary>
public static class VehicleSchedule
{
    public static IQueryable<RoutePlanRouteVehicle> WhereUnfinished(this IQueryable<RoutePlanRouteVehicle> links) => links.Where(x =>
        x.RoutePlanRoute.ActualArrivalAt == null &&
        x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.COMPLETED && x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED && x.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.REJECTED_BY_MANAGER &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
        x.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED);
}
