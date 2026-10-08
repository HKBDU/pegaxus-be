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
            foreach (var id in proposed.Select(x => x.Route.RouteId).Distinct().Order())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Route\" WHERE route_id = {id} FOR UPDATE", token);
            foreach (var id in providerIds)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TransportProvider\" WHERE transport_provider_id = {id} FOR UPDATE", token);
            foreach (var id in vehicleIds)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Vehicle\" WHERE vehicle_id = {id} FOR UPDATE", token);
            foreach (var id in employeeIds)
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Employee\" WHERE employee_id = {id} FOR UPDATE", token);
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
                    throw new ConflictException("Provider must be active.", "PROVIDER_NOT_ACTIVE");
                if (!Modes(provider.SupportedModes).Contains(segment.Route.TransportMode))
                    throw new ConflictException("Provider does not support the segment transport mode.", "PROVIDER_MODE_MISMATCH");
                if (segment.Vehicles.Count > 0 || segment.Driver is not null || segment.Escort is not null)
                    throw new BadRequestException("Outsourced segments cannot use internal vehicles or personnel.");
                continue;
            }
            if (segment.Route.TransportMode != "ROAD")
                throw new BadRequestException("AIR and SEA segments require outsourcing.");
            if (dispatch && (segment.Vehicles.Count == 0 || segment.Driver is null || segment.Escort is null))
                throw new ConflictException("Internal segments require vehicles, a driver and an escort before dispatch.", "RESOURCES_NOT_ASSIGNED");
            var toDate = DateOnly.FromDateTime(segment.PlannedArrivalAt.UtcDateTime);
            foreach (var vehicle in segment.Vehicles)
                if (!vehicles.TryGetValue(vehicle.VehicleId, out var current) || current.IsDeleted || current.TransportMode != TransportMode.ROAD ||
                    current.Status is not (VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED or VehicleStatus.IN_USE) ||
                    current.RegistrationExpiryDate is null || current.InsuranceExpiryDate is null || current.RegistrationExpiryDate < toDate || current.InsuranceExpiryDate < toDate)
                    throw new ConflictException("A vehicle is unavailable or its documents expire before arrival.", "VEHICLE_UNAVAILABLE");
            if (dispatch && segment.Vehicles.Sum(x => vehicles[x.VehicleId].CapacityHorses ?? 0) < horseCount)
                throw new ConflictException("Vehicle capacity is insufficient.", "VEHICLE_CAPACITY_INSUFFICIENT");
            foreach (var (person, role) in new[] { (segment.Driver, EmployeeRole.DRIVER), (segment.Escort, EmployeeRole.ESCORT) })
                if (person is not null && (!employees.TryGetValue(person.EmployeeId, out var current) || !current.IsActive || current.IsDeleted ||
                    current.Role != role || current.Account.IsDeleted || current.Account.Status != AccountStatus.ACTIVE))
                    throw new ConflictException("Assigned personnel are inactive or have an invalid role.", "PERSONNEL_UNAVAILABLE");

            var from = DbTime(segment.PlannedDepartureAt);
            var to = DbTime(segment.PlannedArrivalAt);
            var ids = segment.Vehicles.Select(x => x.VehicleId).ToArray();
            var people = new[] { segment.Driver?.EmployeeId, segment.Escort?.EmployeeId }.OfType<Guid>().ToArray();
            var ownRemaining = remaining.Select(x => x.RoutePlanRouteId).ToArray();
            if (await db.RoutePlanRoutes.AsNoTracking().AnyAsync(x => x.SupersededAt == null && x.ActualArrivalAt == null &&
                !ownRemaining.Contains(x.RoutePlanRouteId) && x.RouteType != RouteType.BACKUP &&
                (x.RoutePlan.Status == RoutePlanStatus.ACTIVE || x.RoutePlan.Status == RoutePlanStatus.CONFIRMED) &&
                x.RoutePlan.PrimaryRoutePlanId == null &&
                (x.ActualDepartureAt != null && x.RoutePlan.Status == RoutePlanStatus.ACTIVE || x.PlannedDepartureAt < to && x.PlannedArrivalAt > from) &&
                (x.RoutePlanRouteVehicles.Any(v => ids.Contains(v.VehicleId)) ||
                    x.DriverEmployeeId.HasValue && people.Contains(x.DriverEmployeeId.Value) ||
                    x.EscortEmployeeId.HasValue && people.Contains(x.EscortEmployeeId.Value)), token))
                throw new ConflictException("Vehicles or personnel overlap another active assignment.", "RESOURCE_SCHEDULE_CONFLICT");
        }
    }

    private async Task<Guid[]> ApplyAsync(RoutePlan plan, RouteChangeRequest change, List<Response.Segment> remaining,
        List<Response.Segment> proposed, bool isReturn, DateTime now, CancellationToken token)
    {
        var oldIds = remaining.Select(x => x.RoutePlanRouteId).ToArray();
        var old = await db.RoutePlanRoutes.Where(x => oldIds.Contains(x.RoutePlanRouteId)).ToListAsync(token);
        foreach (var row in old)
        {
            await db.Entry(row).ReloadAsync(token);
            row.SupersededAt = now;
            row.SupersededByChangeRequestId = change.RouteChangeRequestId;
            row.UpdatedAt = now;
        }
        var sequence = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == plan.RoutePlanId).MaxAsync(x => x.SequenceNo, token);
        var result = new List<Guid>();
        foreach (var segment in proposed)
        {
            var row = new RoutePlanRoute
            {
                RoutePlanRouteId = Guid.NewGuid(), RoutePlanId = plan.RoutePlanId, RouteId = segment.Route.RouteId,
                SequenceNo = ++sequence, RouteType = isReturn ? RouteType.RETURN : RouteType.PRIMARY,
                DriverEmployeeId = segment.Driver?.EmployeeId, EscortEmployeeId = segment.Escort?.EmployeeId,
                TransportProviderId = segment.Outsourcing?.Provider.TransportProviderId,
                ServiceType = segment.Outsourcing?.ServiceType, ProviderReference = segment.Outsourcing?.ProviderReference,
                PlannedDepartureAt = DbTime(segment.PlannedDepartureAt), PlannedArrivalAt = DbTime(segment.PlannedArrivalAt),
                Note = segment.Note, CreatedAt = now, UpdatedAt = now
            };
            db.RoutePlanRoutes.Add(row);
            foreach (var vehicle in segment.Vehicles)
                db.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle { RoutePlanRouteId = row.RoutePlanRouteId, VehicleId = vehicle.VehicleId, AssignedAt = now });
            if (row.TransportProviderId is { } providerId &&
                !db.TransportPlanProviders.Local.Any(x => x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId) &&
                !await db.TransportPlanProviders.AnyAsync(x => x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId, token))
                db.TransportPlanProviders.Add(new TransportPlanProvider
                {
                    TransportPlanId = plan.TransportPlanId, TransportProviderId = providerId, AssignedAt = now, ServiceType = row.ServiceType
                });
            result.Add(row.RoutePlanRouteId);
        }
        var completed = await db.RoutePlanRoutes.AsNoTracking().Where(x => x.RoutePlanId == plan.RoutePlanId && x.SupersededAt == null && !oldIds.Contains(x.RoutePlanRouteId))
            .Include(x => x.Route).ToListAsync(token);
        plan.TotalDistanceKm = completed.Sum(x => x.Route.DistanceKm ?? 0) + proposed.Sum(x => x.Route.DistanceKm!.Value);
        var firstDeparture = completed.OrderBy(x => x.SequenceNo).FirstOrDefault()?.PlannedDepartureAt ?? DbTime(proposed[0].PlannedDepartureAt);
        plan.EstimatedDurationMinutes = checked((int)Math.Ceiling((DbTime(proposed[^1].PlannedArrivalAt) - firstDeparture).TotalMinutes));
        plan.TransportPlan.EstimatedDistanceKm = plan.TotalDistanceKm;
        plan.TransportPlan.EstimatedDurationMinutes = plan.EstimatedDurationMinutes;
        return result.ToArray();
    }

    private void RecordIncident(RoutePlan plan, RouteChangeRequest change, Employee actor, Guid locationId,
        bool isReturn, string? note, DateTime now)
    {
        var incident = new IncidentReport
        {
            IncidentReportId = Guid.NewGuid(), IncidentCode = $"RC-{change.RouteChangeRequestId:N}",
            TransportId = plan.TransportPlan.TransportId, EmployeeId = actor.EmployeeId, LocationId = locationId,
            Category = IncidentCategory.ROUTE_CHANGE, Severity = isReturn ? IncidentSeverity.CRITICAL : IncidentSeverity.HIGH,
            Status = IncidentStatus.REPORTED, Title = isReturn ? "Emergency return - financial settlement required" : "Emergency route change",
            Description = change.Reason, ImmediateAction = note, OccurredAt = now, ReportedAt = now, CreatedAt = now, UpdatedAt = now,
            EmergencySettlementRequired = isReturn
        };
        db.IncidentReports.Add(incident);
        change.IncidentReportId = incident.IncidentReportId;
        if (change.EstimatedExtraCost is { } cost)
            db.EmergencyCostRequests.Add(new EmergencyCostRequest
            {
                EmergencyCostRequestId = Guid.NewGuid(), IncidentReportId = incident.IncidentReportId,
                RequestCode = $"RC-{change.RouteChangeRequestId:N}", Category = EmergencyCostCategory.ROUTE_CHANGE,
                Status = EmergencyCostStatus.PENDING, Description = incident.Title, RequestedAmount = cost, Currency = change.Currency!,
                Justification = change.Reason, RequestedAt = now, CreatedAt = now, UpdatedAt = now
            });
    }

    private async Task<string[]> NotifyDecisionAsync(RoutePlan plan, RouteChangeRequest change, List<Response.Segment> proposed,
        List<Response.Segment> remaining, bool isReturn, DateTime now, CancellationToken token)
    {
        var specialistIds = await db.EmployeeTransportPlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Responsibility == "TRANSPORT_SPECIALIST").Select(x => x.EmployeeId).ToListAsync(token);
        var assignedDrivers = await db.EmployeeTransportPlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Responsibility == "DRIVER").Select(x => x.EmployeeId).ToListAsync(token);
        var driverIds = proposed.Concat(remaining).Where(x => x.Driver is not null).Select(x => x.Driver!.EmployeeId).Concat(assignedDrivers).Distinct().ToArray();
        var employees = await db.Employees.AsNoTracking().Include(x => x.Account).Where(x =>
            specialistIds.Contains(x.EmployeeId) || driverIds.Contains(x.EmployeeId) || isReturn && x.Role == EmployeeRole.LOGISTICS_MANAGER).ToListAsync(token);
        if (!isReturn && !employees.Any(x => specialistIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.TRANSPORT_SPECIALIST && x.IsActive && !x.IsDeleted))
            throw new ConflictException("Assign an active Transport Specialist to update legal documents.", "SPECIALIST_NOT_ASSIGNED");
        var itinerary = string.Join("; ", proposed.Select(x => $"{x.FromLocation.LocationName} - {x.ToLocation.LocationName} ({x.PlannedDepartureAt:O} - {x.PlannedArrivalAt:O})"));
        var title = isReturn ? "Emergency return approved" : "Emergency route change approved";
        var message = $"New ETA: {proposed[^1].PlannedArrivalAt:O}. Start location: {proposed[0].FromLocation.LocationName}. Itinerary: {itinerary}.";
        var accounts = new HashSet<Guid>();
        var notified = new HashSet<string>();
        foreach (var employee in employees.Where(x => x.IsActive && !x.IsDeleted &&
            (specialistIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.TRANSPORT_SPECIALIST ||
                driverIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.DRIVER || isReturn && x.Role == EmployeeRole.LOGISTICS_MANAGER)))
        {
            if (accounts.Add(employee.AccountId))
                Notify(employee.Account, plan.TransportPlan.TransportId, change, title,
                    specialistIds.Contains(employee.EmployeeId) ? message + " Update quarantine and BCP documents urgently." : message, now);
            if (specialistIds.Contains(employee.EmployeeId)) notified.Add("TRANSPORT_SPECIALIST");
            if (driverIds.Contains(employee.EmployeeId)) notified.Add("DRIVER");
            if (isReturn && employee.Role == EmployeeRole.LOGISTICS_MANAGER) notified.Add("LOGISTICS_MANAGER");
        }
        if (plan.TransportPlan.Transport.Booking.Customer.IsDeleted)
            throw new ConflictException("The customer is unavailable.", "RECIPIENT_UNAVAILABLE");
        Notify(plan.TransportPlan.Transport.Booking.Customer.Account, plan.TransportPlan.TransportId, change, title, message, now);
        notified.Add("CUSTOMER");
        return notified.Order().ToArray();
    }
}
