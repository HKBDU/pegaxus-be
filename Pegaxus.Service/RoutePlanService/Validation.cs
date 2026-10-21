using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    private static void ValidateSaveRequest(Request.SaveRequest request, RouteType routeType, bool updating = false)
    {
        if (request.Segments.Count == 0)
            throw new BadRequestException("At least one route segment is required");
        if (request.Action is not ("SAVE_DRAFT" or "SUBMIT_FOR_LEGAL_REVIEW"))
            throw new BadRequestException("action must be SAVE_DRAFT or SUBMIT_FOR_LEGAL_REVIEW");
        if (request.IncidentReportId.HasValue && routeType != RouteType.RETURN)
            throw new BadRequestException("incidentReportId is only allowed for RETURN route plans", "VALIDATION_ERROR");
        if (routeType == RouteType.RETURN && !updating && request.Action != "SAVE_DRAFT")
            throw new BadRequestException("RETURN route plans only support SAVE_DRAFT");
        if (routeType is RouteType.BACKUP or RouteType.RETURN && string.IsNullOrWhiteSpace(request.RouteNote) || routeType is RouteType.BACKUP or RouteType.RETURN && request.RouteNote!.Trim().Length < 10)
            throw new BadRequestException("routeNote is required and must contain at least 10 characters");
        var expected = 1;
        foreach (var segment in request.Segments.OrderBy(item => item.SequenceNo))
        {
            if (segment.SequenceNo != expected++)
                throw new BadRequestException("sequenceNo must be contiguous starting from 1");
            if (segment.PlannedDepartureAt == default || segment.PlannedArrivalAt == default || segment.PlannedArrivalAt <= segment.PlannedDepartureAt)
                throw new BadRequestException("plannedArrivalAt must be after plannedDepartureAt");
            if (segment.RouteId is null && (segment.FromLocationId is null || segment.ToLocationId is null || string.IsNullOrWhiteSpace(segment.TransportMode)))
                throw new BadRequestException("routeId or fromLocationId, toLocationId and transportMode is required");
            if (segment.RouteId is not null && (segment.FromLocationId is not null || segment.ToLocationId is not null || !string.IsNullOrWhiteSpace(segment.TransportMode)))
                throw new BadRequestException("Use either routeId or the route location/mode fields, not both");
            if (segment.Outsourcing is not null && segment.VehicleIds.Count > 0)
                throw new UnprocessableEntityException("An outsourced segment cannot have vehicles, driver or escort", "OUTSOURCED_SEGMENT");
        }
        var types = request.Segments.Select(segment => SegmentType(segment, routeType, null)).ToList();
        var allowed = routeType switch
        {
            RouteType.PRIMARY => types.All(type => type == RouteType.PRIMARY),
            RouteType.RETURN => types.All(type => type == RouteType.RETURN),
            _ => types.All(type => type is RouteType.PRIMARY or RouteType.BACKUP) && types.Contains(RouteType.BACKUP)
        };
        if (!allowed)
            throw new BadRequestException(routeType switch
            {
                RouteType.PRIMARY => "Every segment of a PRIMARY route plan must have routeType PRIMARY",
                RouteType.RETURN => "Every segment of a RETURN route plan must have routeType RETURN",
                _ => "A BACKUP route plan needs at least one BACKUP segment and every segment must be PRIMARY or BACKUP"
            });
    }

    private static RouteType SegmentType(Request.SegmentRequest segment, RouteType planType, IReadOnlyDictionary<int, RouteType>? existing) =>
        !string.IsNullOrWhiteSpace(segment.RouteType) ? ParseRouteType(segment.RouteType)
        : existing is not null && existing.TryGetValue(segment.SequenceNo, out var current) ? current
        : planType;

    private async Task<List<MaterializedSegment>> MaterializeSegmentsAsync(IReadOnlyCollection<Request.SegmentRequest> requests, RouteType routeType, TransportPlan plan, Guid? excludedRoutePlanId, Func<Request.SegmentRequest, RouteType> typeOf, CancellationToken cancellationToken)
    {
        var result = new List<MaterializedSegment>();
        foreach (var request in requests.OrderBy(item => item.SequenceNo))
        {
            Route route;
            if (request.RouteId.HasValue)
            {
                route = await db.Routes.Include(item => item.FromLocation).Include(item => item.ToLocation)
                    .SingleOrDefaultAsync(item => item.RouteId == request.RouteId.Value && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Route not found", "ROUTE_NOT_FOUND");
            }
            else
            {
                var mode = ParseTransportMode(request.TransportMode!);
                var from = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.FromLocationId && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
                var to = await db.Locations.SingleOrDefaultAsync(item => item.LocationId == request.ToLocationId && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
                route = await db.Routes.FirstOrDefaultAsync(item => item.FromLocationId == from.LocationId && item.ToLocationId == to.LocationId && item.TransportMode == mode && !item.IsDeleted, cancellationToken)
                    ?? new Route
                    {
                        RouteId = Guid.NewGuid(),
                        RouteCode = $"RT-AUTO-{Guid.NewGuid():N}"[..20],
                        FromLocationId = from.LocationId,
                        ToLocationId = to.LocationId,
                        TransportMode = mode,
                        RouteName = $"{from.LocationName} - {to.LocationName}",
                        CreatedAt = UtcNowTruncated(),
                        UpdatedAt = UtcNowTruncated()
                    };
                if (route.RouteId != Guid.Empty && db.Entry(route).State == EntityState.Detached)
                    db.Routes.Add(route);
                route.FromLocation = from;
                route.ToLocation = to;
                if (!route.DistanceKm.HasValue || !route.EstimatedDurationMinutes.HasValue)
                {
                    var metrics = await routeService.CalculateMetricsAsync(from, to, mode, cancellationToken);
                    route.DistanceKm = metrics.DistanceKm;
                    route.EstimatedDurationMinutes = metrics.DurationMinutes;
                }
            }
            await ValidateVehicleAndProviderAsync(request, route, plan, excludedRoutePlanId, routeType, cancellationToken);
            result.Add(new MaterializedSegment(request, route, typeOf(request)));
        }
        return result;
    }

    private async Task ValidateVehicleAndProviderAsync(Request.SegmentRequest request, Route route, TransportPlan plan, Guid? excludedRoutePlanId, RouteType routeType, CancellationToken cancellationToken)
    {
        if (request.Outsourcing is not null)
        {
            var provider = db.TransportProviders.SingleOrDefault(item => item.TransportProviderId == request.Outsourcing.TransportProviderId && !item.IsDeleted);
            if (provider is null)
                throw new NotFoundException("Transport Provider not found", "TRANSPORT_PROVIDER_NOT_FOUND");
            if (!SupportsMode(provider.SupportedModes, route.TransportMode))
                throw new UnprocessableEntityException("The provider does not support the transport mode of this segment", "PROVIDER_MODE_MISMATCH");
        }
        if (request.VehicleIds.Count == 0)
            return;
        var vehicles = db.Vehicles.Where(item => request.VehicleIds.Contains(item.VehicleId) && !item.IsDeleted).ToList();
        if (vehicles.Count != request.VehicleIds.Distinct().Count())
            throw new NotFoundException("Vehicle not found", "VEHICLE_NOT_FOUND");
        if (vehicles.Any(item => item.TransportMode != route.TransportMode))
            throw new UnprocessableEntityException("The vehicle transport mode does not match the segment", "VEHICLE_MODE_MISMATCH");
        // A RETURN continues the trip, so the vehicles already driving this plan's primary route are still usable.
        var ownVehicleIds = routeType == RouteType.RETURN
            ? await db.RoutePlanRouteVehicles.Where(item => item.RoutePlanRoute.RoutePlan.TransportPlanId == plan.TransportPlanId)
                .Select(item => item.VehicleId).Distinct().ToListAsync(cancellationToken)
            : [];
        if (vehicles.Any(item => item.Status is not (VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED) &&
                !(item.Status == VehicleStatus.IN_USE && ownVehicleIds.Contains(item.VehicleId))))
            throw new UnprocessableEntityException("The vehicle is not available", "VEHICLE_UNAVAILABLE");
        if (vehicles.Sum(item => item.CapacityHorses ?? 0) < plan.Transport.Booking.BookingHorses.Count)
            throw new UnprocessableEntityException("Total vehicle capacity is lower than the number of horses in the booking", "VEHICLE_CAPACITY_EXCEEDED");
        var departureDate = DateOnly.FromDateTime(request.PlannedDepartureAt.UtcDateTime);
        if (vehicles.Any(item =>
                item.RegistrationExpiryDate.HasValue && item.RegistrationExpiryDate.Value < departureDate ||
                item.InsuranceExpiryDate.HasValue && item.InsuranceExpiryDate.Value < departureDate))
            throw new UnprocessableEntityException("The vehicle registration or insurance is expired for this segment", "VEHICLE_DOCUMENT_EXPIRED");

        var vehicleIds = vehicles.Select(item => item.VehicleId).ToList();
        var departureAt = Truncate(request.PlannedDepartureAt);
        var arrivalAt = Truncate(request.PlannedArrivalAt);
        var overlappingAssignment = await db.RoutePlanRouteVehicles
            .Include(item => item.RoutePlanRoute)
                .ThenInclude(item => item.RoutePlan)
            .Where(item => vehicleIds.Contains(item.VehicleId) &&
                item.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED && item.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.REJECTED_BY_MANAGER &&
                (!excludedRoutePlanId.HasValue || item.RoutePlanRoute.RoutePlanId != excludedRoutePlanId.Value) &&
                item.RoutePlanRoute.RoutePlan.TransportPlanId != plan.TransportPlanId &&
                item.RoutePlanRoute.PlannedDepartureAt < arrivalAt &&
                item.RoutePlanRoute.PlannedArrivalAt > departureAt)
            .Select(item => item.VehicleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (overlappingAssignment != Guid.Empty)
            throw new ConflictException("The vehicle is already assigned during this time slot", "VEHICLE_SCHEDULE_CONFLICT");
    }

    private static void ValidateRouteRules(IReadOnlyList<MaterializedSegment> segments, TransportPlan plan, RouteType routeType, RoutePlan? primary, Guid? updatingRoutePlanId = null)
    {
        for (var index = 1; index < segments.Count; index++)
        {
            if (segments[index - 1].Route.ToLocationId != segments[index].Route.FromLocationId)
                throw new UnprocessableEntityException("Segments must be continuous: each segment must start where the previous one ends", "SEGMENT_NOT_CONTINUOUS");
            if (segments[index].Request.PlannedDepartureAt < segments[index - 1].Request.PlannedArrivalAt)
                throw new UnprocessableEntityException("A segment cannot depart before the previous segment arrives", "SEGMENT_TIME_OVERLAP");
        }
        if (plan.Transport.Booking.OriginCountry != plan.Transport.Booking.DestinationCountry && !segments.Any(item =>
                item.Route.FromLocation.LocationType is LocationType.BORDER_GATE or LocationType.AIRPORT or LocationType.SEAPORT ||
                item.Route.ToLocation.LocationType is LocationType.BORDER_GATE or LocationType.AIRPORT or LocationType.SEAPORT))
            throw new UnprocessableEntityException("A cross-border transport requires at least one border control point", "BCP_REQUIRED");
        var restFailure = RouteRestRules.Evaluate(segments.Select(item => new RouteRestRules.Leg(
            item.Request.PlannedDepartureAt.UtcDateTime, item.Request.PlannedArrivalAt.UtcDateTime, item.Route.TransportMode,
            item.Route.FromLocation.LocationType, item.Route.ToLocation.LocationType)).ToArray()).FirstOrDefault(check => !check.Passed);
        if (restFailure is not null)
            throw new UnprocessableEntityException(restFailure.Message, "REST_RULE_VIOLATION");
        if (routeType == RouteType.BACKUP && primary is not null)
        {
            var primarySegments = primary.RoutePlanRoutes.OrderBy(item => item.SequenceNo).ToList();
            if (primarySegments.Count > 0 &&
                (segments[0].Route.FromLocationId != primarySegments[0].Route.FromLocationId ||
                 segments[^1].Route.ToLocationId != primarySegments[^1].Route.ToLocationId))
                throw new UnprocessableEntityException("The backup route must start and end at the same locations as the primary route", "BACKUP_NOT_ANCHORED");
            var primaryRouteIds = primarySegments.Select(item => item.RouteId).ToHashSet();
            if (segments.Any(item => item.Type == RouteType.PRIMARY && !primaryRouteIds.Contains(item.Route.RouteId)))
                throw new UnprocessableEntityException("A primary segment in the backup route must match a segment of the primary route", "BACKUP_SEGMENT_MISMATCH");
            if (primarySegments.Select(item => item.RouteId).SequenceEqual(segments.Select(item => item.Route.RouteId)))
                throw new ConflictException("Contingency route cannot be identical to the primary route. Please select alternative waypoints or border checkpoints.", "IDENTICAL_CONTINGENCY_ROUTE");
        }
    }

    private static void EnsureWriteRole(EmployeeRole role, RouteType type)
    {
        if (role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            return;
        if (role == EmployeeRole.LOGISTICS_MANAGER && type is RouteType.BACKUP or RouteType.RETURN)
            return;
        throw new ForbiddenException("The current role cannot manage this route plan type");
    }

    private static void EnsureReadRole(EmployeeRole role, TransportPlan plan, Guid employeeId)
    {
        if (role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only RC, LM and TS can access Route Plans");
        if (role == EmployeeRole.TRANSPORT_SPECIALIST && !plan.EmployeeTransportPlans.Any(item => item.EmployeeId == employeeId))
            throw new ForbiddenException("You are not assigned to this Transport Plan");
    }

    private static async Task EnsurePlanCanBeUsedAsync(TransportPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Transport.Booking.Status != BookingStatus.APPROVED)
            throw new ConflictException($"This action cannot be performed while the booking is in {plan.Transport.Booking.Status} status", "INVALID_BOOKING_STATUS");
        if (plan.Status != TransportPlanStatus.APPROVED)
            throw new ConflictException("Route Plan requires an approved Transport Plan", "INVALID_TRANSPORT_PLAN_STATUS");
        await Task.CompletedTask;
    }

    private static RouteType ParseRouteType(string value) => EnumParser.TryParseDefined<RouteType>(value, out var result) && result is RouteType.PRIMARY or RouteType.BACKUP or RouteType.RETURN
        ? result
        : throw new BadRequestException("routeType must be PRIMARY, BACKUP or RETURN");

    private static TransportMode ParseTransportMode(string value) => EnumParser.TryParseDefined<TransportMode>(value, out var result)
        ? result
        : throw new BadRequestException("transportMode is invalid");

    private static bool SupportsMode(string? supportedModes, TransportMode mode) => string.IsNullOrWhiteSpace(supportedModes) || supportedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(item => item.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase));

    private sealed record MaterializedSegment(Request.SegmentRequest Request, Route Route, RouteType Type);
}
