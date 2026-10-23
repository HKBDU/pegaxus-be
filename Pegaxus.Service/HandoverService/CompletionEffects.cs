using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service
{
    private async Task<(Transport Transport, RoutePlanRoute[] Segments)> LockJourneyAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var transport = await db.Transports.SingleAsync(item => item.TransportId == transportId, cancellationToken);
        await db.Entry(transport).ReloadAsync(cancellationToken);
        var planIds = await db.TransportPlans.Where(plan => plan.TransportId == transportId).OrderBy(plan => plan.TransportPlanId).Select(plan => plan.TransportPlanId).ToArrayAsync(cancellationToken);
        foreach (var id in planIds)
            await db.LockRowsAsync(db.TransportPlans.Where(plan => plan.TransportPlanId == id), plan => plan.TransportPlanId, cancellationToken);
        foreach (var plan in await db.TransportPlans.Where(plan => plan.TransportId == transportId).ToArrayAsync(cancellationToken))
            await db.Entry(plan).ReloadAsync(cancellationToken);
        var routeIds = await db.RoutePlans.Where(plan => plan.TransportPlan.TransportId == transportId).OrderBy(plan => plan.RoutePlanId).Select(plan => plan.RoutePlanId).ToArrayAsync(cancellationToken);
        foreach (var id in routeIds)
            await db.LockRowsAsync(db.RoutePlans.Where(plan => plan.RoutePlanId == id), plan => plan.RoutePlanId, cancellationToken);
        foreach (var plan in await db.RoutePlans.Where(plan => plan.TransportPlan.TransportId == transportId).ToArrayAsync(cancellationToken))
            await db.Entry(plan).ReloadAsync(cancellationToken);
        var segmentIds = await db.RoutePlanRoutes.Where(segment => segment.RoutePlan.TransportPlan.TransportId == transportId)
            .OrderBy(segment => segment.RoutePlanRouteId).Select(segment => segment.RoutePlanRouteId).ToArrayAsync(cancellationToken);
        foreach (var id in segmentIds)
            await db.LockRowsAsync(db.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == id), segment => segment.RoutePlanRouteId, cancellationToken);
        var segments = await db.RoutePlanRoutes.Where(segment => segment.RoutePlan.TransportPlan.TransportId == transportId).ToArrayAsync(cancellationToken);
        foreach (var segment in segments) await db.Entry(segment).ReloadAsync(cancellationToken);
        return (transport, segments);
    }

    private static void ValidateBoundary(HandoverRecord item, RoutePlanRoute[] segments, RoutePlanRoute[] effective, Guid customerId)
    {
        if (item.HandoverType == HandoverType.CARRIER_HANDOVER)
            throw new UnprocessableEntityException("Provider handovers are not supported by the current database schema", "HANDOVER_TYPE_NOT_SUPPORTED");
        var from = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == item.FromRoutePlanRouteId);
        var to = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == item.ToRoutePlanRouteId);
        if (item.HandoverType != HandoverType.OTHER && (from?.TransportProviderId is not null || to?.TransportProviderId is not null))
            throw new UnprocessableEntityException("Outsourced segments require a provider handover", item.HandoverType is HandoverType.TRANSFER or HandoverType.BORDER_HANDOVER ? "OUTSOURCED_SEGMENT" : "HANDOVER_TYPE_NOT_SUPPORTED");
        var fromIndex = Array.FindIndex(effective, segment => segment.RoutePlanRouteId == item.FromRoutePlanRouteId);
        var toIndex = Array.FindIndex(effective, segment => segment.RoutePlanRouteId == item.ToRoutePlanRouteId);
        var valid = item.HandoverType switch
        {
            HandoverType.PICKUP => item.FromRoutePlanRouteId is null && toIndex == 0 &&
                CustomerParty(item.FromType, item.FromCustomerId, item.FromEmployeeId, customerId) && EmployeeParty(item.ToType, item.ToCustomerId, item.ToEmployeeId, to?.DriverEmployeeId),
            HandoverType.TRANSFER or HandoverType.BORDER_HANDOVER => fromIndex >= 0 && toIndex == fromIndex + 1 &&
                EmployeeParty(item.FromType, item.FromCustomerId, item.FromEmployeeId, from?.DriverEmployeeId) && EmployeeParty(item.ToType, item.ToCustomerId, item.ToEmployeeId, to?.DriverEmployeeId),
            HandoverType.DELIVERY => item.ToRoutePlanRouteId is null && fromIndex >= 0 && fromIndex == effective.Length - 1 &&
                EmployeeParty(item.FromType, item.FromCustomerId, item.FromEmployeeId, from?.DriverEmployeeId) && CustomerParty(item.ToType, item.ToCustomerId, item.ToEmployeeId, customerId),
            HandoverType.OTHER => (item.FromRoutePlanRouteId is null || from is not null) && (item.ToRoutePlanRouteId is null || to is not null) &&
                ValidParty(item.FromType, item.FromCustomerId, item.FromEmployeeId) && ValidParty(item.ToType, item.ToCustomerId, item.ToEmployeeId),
            _ => false
        };
        if (valid && item.HandoverType is HandoverType.TRANSFER or HandoverType.BORDER_HANDOVER && !HandoverRules.RequiredBetween(from!.DriverEmployeeId, to!.DriverEmployeeId))
            throw new UnprocessableEntityException("The same driver runs both segments, so no handover record is needed here", "HANDOVER_NOT_REQUIRED");
        if (!valid) throw new UnprocessableEntityException("The handover segment boundary or parties no longer match the effective route", "INVALID_SEGMENT_PAIR");
    }

    private static bool CustomerParty(HandoverPartyType type, Guid? customerId, Guid? employeeId, Guid expected) => type == HandoverPartyType.CUSTOMER && customerId == expected && employeeId is null;
    private static bool EmployeeParty(HandoverPartyType type, Guid? customerId, Guid? employeeId, Guid? expected) => type == HandoverPartyType.EMPLOYEE && expected.HasValue && employeeId == expected && customerId is null;
    private static bool ValidParty(HandoverPartyType type, Guid? customerId, Guid? employeeId) => type switch
    { HandoverPartyType.CUSTOMER => customerId.HasValue && employeeId is null, HandoverPartyType.EMPLOYEE => employeeId.HasValue && customerId is null, _ => false };

    private async Task<Response.CompletionEffects> ApplyEffectsAsync(HandoverRecord item, Transport transport, RoutePlanRoute[] segments, RoutePlanRoute[] effective, DateTime now, CancellationToken cancellationToken)
    {
        var from = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == item.FromRoutePlanRouteId);
        var to = segments.SingleOrDefault(segment => segment.RoutePlanRouteId == item.ToRoutePlanRouteId);
        if (item.HandoverType != HandoverType.OTHER)
        {
            // The signing time is the official handover moment, so it replaces any time written earlier (M6).
            if (from is not null) { from.ActualArrivalAt = now; from.UpdatedAt = now; }
            if (to is not null) { to.ActualDepartureAt = now; to.UpdatedAt = now; }
        }
        if (item.HandoverType == HandoverType.PICKUP)
        {
            if (transport.Status == TransportStatus.READY) { transport.Status = TransportStatus.IN_TRANSIT; transport.UpdatedAt = now; }
            transport.ActualDepartureAt = now; transport.UpdatedAt = now;
            if (to!.RoutePlan.Status == RoutePlanStatus.CONFIRMED) { to.RoutePlan.Status = RoutePlanStatus.ACTIVE; to.RoutePlan.UpdatedAt = now; }
            if (to.RoutePlan.TransportPlan.Status == TransportPlanStatus.APPROVED) { to.RoutePlan.TransportPlan.Status = TransportPlanStatus.IN_PROGRESS; to.RoutePlan.TransportPlan.UpdatedAt = now; }
        }
        if (item.HandoverType == HandoverType.DELIVERY)
        {
            if (transport.Status == TransportStatus.IN_TRANSIT) { transport.Status = TransportStatus.COMPLETED; transport.CompletedAt = now; transport.UpdatedAt = now; }
            transport.ActualArrivalAt = now; transport.UpdatedAt = now;
            foreach (var plan in segments.Select(segment => segment.RoutePlan).DistinctBy(plan => plan.RoutePlanId).Where(plan => plan.Status == RoutePlanStatus.ACTIVE))
            {
                plan.Status = RoutePlanStatus.COMPLETED; plan.UpdatedAt = now;
                if (plan.TransportPlan.Status == TransportPlanStatus.IN_PROGRESS) { plan.TransportPlan.Status = TransportPlanStatus.COMPLETED; plan.TransportPlan.UpdatedAt = now; }
            }
        }
        var released = new List<Response.ReleasedVehicle>();
        if (item.HandoverType is HandoverType.PICKUP or HandoverType.DELIVERY)
        {
            var segmentIds = (item.HandoverType == HandoverType.PICKUP ? effective : segments).Select(segment => segment.RoutePlanRouteId).ToArray();
            var vehicleIds = await db.RoutePlanRouteVehicles.Where(link => segmentIds.Contains(link.RoutePlanRouteId)).Select(link => link.VehicleId).Distinct().OrderBy(id => id).ToArrayAsync(cancellationToken);
            foreach (var id in vehicleIds) await db.LockRowsAsync(db.Vehicles.Where(vehicle => vehicle.VehicleId == id), vehicle => vehicle.VehicleId, cancellationToken);
            var busy = await db.RoutePlanRouteVehicles.AsNoTracking().Where(link => vehicleIds.Contains(link.VehicleId) &&
                    link.RoutePlanRoute.RoutePlan.TransportPlan.TransportId != transport.TransportId && link.RoutePlanRoute.ActualArrivalAt == null &&
                    (link.RoutePlanRoute.SupersededAt == null || link.RoutePlanRoute.ActualDepartureAt != null) &&
                    link.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.COMPLETED && link.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED && link.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.REJECTED_BY_MANAGER &&
                    link.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED && link.RoutePlanRoute.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED &&
                    link.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED && link.RoutePlanRoute.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED)
                .Select(link => link.VehicleId).Distinct().ToArrayAsync(cancellationToken);
            // O-F6-19: a vehicle that this trip left needing repair goes to maintenance instead of back to the pool.
            var needsRepair = item.HandoverType == HandoverType.DELIVERY
                ? await db.VehicleIncidentDetails.AsNoTracking().Where(detail => detail.RepairRequired && vehicleIds.Contains(detail.VehicleId) &&
                        detail.IncidentReport.TransportId == transport.TransportId && detail.IncidentReport.Status != IncidentStatus.CANCELLED)
                    .Select(detail => detail.VehicleId).Distinct().ToArrayAsync(cancellationToken)
                : [];
            foreach (var vehicle in await db.Vehicles.Where(vehicle => vehicleIds.Contains(vehicle.VehicleId)).OrderBy(vehicle => vehicle.VehicleCode).ToArrayAsync(cancellationToken))
            {
                await db.Entry(vehicle).ReloadAsync(cancellationToken);
                if (!vehicle.IsDeleted && item.HandoverType == HandoverType.PICKUP && vehicle.Status == VehicleStatus.ASSIGNED) { vehicle.Status = VehicleStatus.IN_USE; vehicle.UpdatedAt = now; }
                if (!vehicle.IsDeleted && item.HandoverType == HandoverType.DELIVERY && vehicle.Status == VehicleStatus.IN_USE && !busy.Contains(vehicle.VehicleId)) {
                    vehicle.Status = needsRepair.Contains(vehicle.VehicleId) ? VehicleStatus.MAINTENANCE : VehicleStatus.AVAILABLE; vehicle.UpdatedAt = now;
                    released.Add(new(vehicle.VehicleId, vehicle.VehicleCode, vehicle.LicensePlate, vehicle.Status.ToString()));
                }
            }
        }
        var route = (to ?? from)?.RoutePlan;
        return new(transport.Status.ToString(), route?.Status.ToString(), route?.TransportPlan.Status.ToString(),
            item.HandoverType == HandoverType.OTHER ? null : from?.RoutePlanRouteId, item.HandoverType == HandoverType.OTHER ? null : to?.RoutePlanRouteId,
            item.HandoverType == HandoverType.DELIVERY ? AsUtc(now.AddHours(24)) : null,
            item.HandoverType == HandoverType.DELIVERY ? !string.IsNullOrWhiteSpace(item.DiscrepancyNote) : null,
            item.HandoverType == HandoverType.DELIVERY ? released : null);
    }
}
