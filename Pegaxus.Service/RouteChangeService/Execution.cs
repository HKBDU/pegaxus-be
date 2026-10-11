using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service
{
    private async Task ValidateResourcesAsync(List<Response.Segment> proposed, RoutePlan plan,
        List<Response.Segment> remaining, bool dispatch, CancellationToken token)
    {
        var vehicleIds = proposed.SelectMany(x => x.Vehicles.Select(v => v.VehicleId)).Distinct().Order().ToArray();
        var employeeIds = proposed.SelectMany(x => new[] { x.Driver?.EmployeeId, x.Escort?.EmployeeId }).OfType<Guid>().Distinct().Order().ToArray();
        var providerIds = proposed.Where(x => x.Outsourcing is not null).Select(x => x.Outsourcing!.Provider.TransportProviderId).Distinct().Order().ToArray();
        if (db.Database.IsRelational())
        {
            // Updating each key to itself holds row locks in a stable order until the transaction ends.
            foreach (var id in proposed.Select(x => x.Route.RouteId).Distinct().Order())
                await db.Routes.Where(x => x.RouteId == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RouteId, x => x.RouteId), token);
            foreach (var id in providerIds)
                await db.TransportProviders.Where(x => x.TransportProviderId == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportProviderId, x => x.TransportProviderId), token);
            foreach (var id in vehicleIds)
                await db.Vehicles.Where(x => x.VehicleId == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.VehicleId, x => x.VehicleId), token);
            foreach (var id in employeeIds)
                await db.Employees.Where(x => x.EmployeeId == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EmployeeId, x => x.EmployeeId), token);
        }
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => vehicleIds.Contains(x.VehicleId)).ToDictionaryAsync(x => x.VehicleId, token);
        var employees = await db.Employees.AsNoTracking().Include(x => x.Account).Where(x => employeeIds.Contains(x.EmployeeId)).ToDictionaryAsync(x => x.EmployeeId, token);
        var providers = await db.TransportProviders.AsNoTracking().Where(x => providerIds.Contains(x.TransportProviderId)).ToDictionaryAsync(x => x.TransportProviderId, token);
        var horseCount = await db.BookingHorses.CountAsync(x => x.BookingId == plan.TransportPlan.Transport.BookingId, token);
        foreach (var segment in proposed)
        {
            var route = await db.Routes.AsNoTracking().Include(x => x.FromLocation).Include(x => x.ToLocation)
                .SingleOrDefaultAsync(x => x.RouteId == segment.Route.RouteId, token);
            if (route is null || route.IsDeleted || route.FromLocation.IsDeleted || route.ToLocation.IsDeleted ||
                route.FromLocationId != segment.FromLocation.LocationId || route.ToLocationId != segment.ToLocation.LocationId ||
                route.TransportMode.ToString() != segment.Route.TransportMode || route.DistanceKm != segment.Route.DistanceKm ||
                route.FromLocation.LocationType?.ToString() != segment.FromLocation.LocationType ||
                route.ToLocation.LocationType?.ToString() != segment.ToLocation.LocationType)
                throw new ConflictException("A proposed route master changed or is unavailable.", "ROUTE_MASTER_MODIFIED");
            if (segment.Outsourcing is { } outsourced)
            {
                if (!providers.TryGetValue(outsourced.Provider.TransportProviderId, out var provider) || provider.IsDeleted || provider.Status != ProviderStatus.ACTIVE)
                    throw new ConflictException("Provider must be active.", "PROVIDER_INACTIVE");
                if (!Modes(provider.SupportedModes).Contains(segment.Route.TransportMode))
                    throw new UnprocessableEntityException("Provider does not support the segment transport mode.", "PROVIDER_MODE_MISMATCH");
                if (segment.Vehicles.Count > 0 || segment.Driver is not null || segment.Escort is not null)
                    throw new UnprocessableEntityException("Outsourced segments cannot use internal vehicles or personnel.", "OUTSOURCED_SEGMENT");
                continue;
            }
            if (dispatch && (segment.Driver is null || segment.Escort is null))
                throw new ConflictException("Internal segments require vehicles, a driver and an escort before dispatch.", "RESOURCES_NOT_ASSIGNED");
            var toDate = DateOnly.FromDateTime(segment.PlannedArrivalAt.UtcDateTime);
            foreach (var vehicle in segment.Vehicles)
            {
                if (vehicles.TryGetValue(vehicle.VehicleId, out var modeVehicle) && modeVehicle.TransportMode.ToString() != segment.Route.TransportMode)
                    throw new UnprocessableEntityException("Vehicle mode does not match the segment.", "VEHICLE_MODE_MISMATCH");
                if (vehicles.TryGetValue(vehicle.VehicleId, out var inUse) && inUse.Status == VehicleStatus.IN_USE &&
                    !await db.RoutePlanRouteVehicles.AnyAsync(x => x.VehicleId == vehicle.VehicleId &&
                        x.RoutePlanRoute.RoutePlan.TransportPlanId == plan.TransportPlanId && x.RoutePlanRoute.RouteType == RouteType.PRIMARY, token))
                    throw new UnprocessableEntityException("An IN_USE vehicle must belong to this transport's PRIMARY.", "VEHICLE_UNAVAILABLE");
                if (!vehicles.TryGetValue(vehicle.VehicleId, out var current) || current.IsDeleted || current.TransportMode.ToString() != segment.Route.TransportMode ||
                    current.Status is not (VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED or VehicleStatus.IN_USE) ||
                    current.RegistrationExpiryDate <= toDate || current.InsuranceExpiryDate <= toDate)
                    throw new UnprocessableEntityException("A vehicle is unavailable or its documents expire before arrival.", "VEHICLE_UNAVAILABLE");
            }
            if (dispatch && (segment.Vehicles.Count == 0 || segment.Vehicles.Sum(x => vehicles[x.VehicleId].CapacityHorses ?? 0) < horseCount))
                throw new UnprocessableEntityException("Vehicle capacity is insufficient.", "VEHICLE_CAPACITY_EXCEEDED");
            foreach (var (person, role) in new[] { (segment.Driver, EmployeeRole.DRIVER), (segment.Escort, EmployeeRole.ESCORT) })
                if (person is not null && (!employees.TryGetValue(person.EmployeeId, out var current) || !current.IsActive || current.IsDeleted ||
                    current.Role != role || current.Account.IsDeleted || current.Account.Status != AccountStatus.ACTIVE))
                    throw new ConflictException("Assigned personnel are inactive or have an invalid role.", "PERSONNEL_UNAVAILABLE");

            var from = DbTime(segment.PlannedDepartureAt);
            var to = DbTime(segment.PlannedArrivalAt);
            var ids = segment.Vehicles.Select(x => x.VehicleId).ToArray();
            var people = new[] { segment.Driver?.EmployeeId, segment.Escort?.EmployeeId }.OfType<Guid>().ToArray();
            if (proposed.Any(x => x.RoutePlanRouteId != segment.RoutePlanRouteId && x.ExecutionType == "INTERNAL" &&
                x.PlannedDepartureAt <= segment.PlannedArrivalAt && x.PlannedArrivalAt >= segment.PlannedDepartureAt &&
                (x.Vehicles.Any(v => ids.Contains(v.VehicleId)) || x.Driver is not null && people.Contains(x.Driver.EmployeeId) ||
                    x.Escort is not null && people.Contains(x.Escort.EmployeeId))))
                throw new ConflictException("Resources overlap within this itinerary.", "SCHEDULE_CONFLICT");
            var ownRemaining = remaining.Select(x => x.RoutePlanRouteId).ToArray();
            if (await db.RoutePlanRoutes.AsNoTracking().AnyAsync(x => x.SupersededAt == null && x.ActualArrivalAt == null &&
                !ownRemaining.Contains(x.RoutePlanRouteId) && x.RoutePlanId != plan.RoutePlanId &&
                (x.RoutePlan.Status == RoutePlanStatus.ACTIVE || x.RoutePlan.Status == RoutePlanStatus.CONFIRMED && x.RouteType != RouteType.BACKUP) &&
                (!x.PlannedDepartureAt.HasValue || !x.PlannedArrivalAt.HasValue || x.PlannedArrivalAt <= x.PlannedDepartureAt ||
                 x.ActualDepartureAt != null && x.RoutePlan.Status == RoutePlanStatus.ACTIVE || x.PlannedDepartureAt <= to && x.PlannedArrivalAt >= from) &&
                (x.RoutePlanRouteVehicles.Any(v => ids.Contains(v.VehicleId)) ||
                    x.DriverEmployeeId.HasValue && people.Contains(x.DriverEmployeeId.Value) ||
                    x.EscortEmployeeId.HasValue && people.Contains(x.EscortEmployeeId.Value)), token))
                throw new ConflictException("Vehicles or personnel overlap another active assignment.", "SCHEDULE_CONFLICT");
        }
    }

}
