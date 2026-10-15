using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;

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
    }

    private async Task<List<MaterializedSegment>> MaterializeSegmentsAsync(IReadOnlyCollection<Request.SegmentRequest> requests, RouteType routeType, TransportPlan plan, Guid? excludedRoutePlanId, CancellationToken cancellationToken)
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
            }
            await ValidateVehicleAndProviderAsync(request, route, plan, excludedRoutePlanId, cancellationToken);
            result.Add(new MaterializedSegment(request, route));
        }
        return result;
    }

    private async Task ValidateVehicleAndProviderAsync(Request.SegmentRequest request, Route route, TransportPlan plan, Guid? excludedRoutePlanId, CancellationToken cancellationToken)
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
        if (vehicles.Any(item => item.Status is not (VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED)))
            throw new UnprocessableEntityException("The vehicle is not available", "VEHICLE_UNAVAILABLE");
        if (vehicles.Sum(item => item.CapacityHorses ?? 0) < plan.Transport.Booking.BookingHorses.Count)
            throw new UnprocessableEntityException("Total vehicle capacity is lower than the number of horses in the booking", "VEHICLE_CAPACITY_EXCEEDED");
        var departureDate = DateOnly.FromDateTime(request.PlannedDepartureAt.UtcDateTime);
        if (vehicles.Any(item =>
                item.RegistrationExpiryDate.HasValue && item.RegistrationExpiryDate.Value < departureDate ||
                item.InsuranceExpiryDate.HasValue && item.InsuranceExpiryDate.Value < departureDate))
            throw new UnprocessableEntityException("The vehicle registration or insurance is expired for this segment", "VEHICLE_DOCUMENT_EXPIRED");

        var vehicleIds = vehicles.Select(item => item.VehicleId).ToList();
        var overlappingAssignment = await db.RoutePlanRouteVehicles
            .Include(item => item.RoutePlanRoute)
                .ThenInclude(item => item.RoutePlan)
            .Where(item => vehicleIds.Contains(item.VehicleId) &&
                item.RoutePlanRoute.RoutePlan.Status != RoutePlanStatus.CANCELLED &&
                (!excludedRoutePlanId.HasValue || item.RoutePlanRoute.RoutePlanId != excludedRoutePlanId.Value) &&
                item.RoutePlanRoute.PlannedDepartureAt < request.PlannedArrivalAt.UtcDateTime &&
                item.RoutePlanRoute.PlannedArrivalAt > request.PlannedDepartureAt.UtcDateTime)
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
        if (segments.Any(item => item.Route.TransportMode == TransportMode.ROAD && item.Request.PlannedArrivalAt - item.Request.PlannedDepartureAt > TimeSpan.FromHours(8)))
            throw new UnprocessableEntityException("Maximum continuous driving time is 8 hours", "REST_RULE_VIOLATION");
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.Route.TransportMode != TransportMode.ROAD || segment.Request.PlannedArrivalAt - segment.Request.PlannedDepartureAt <= TimeSpan.FromMinutes(270))
                continue;
            var restAfter = index < segments.Count - 1
                ? segments[index + 1].Request.PlannedDepartureAt - segment.Request.PlannedArrivalAt
                : TimeSpan.Zero;
            if (restAfter < TimeSpan.FromMinutes(45))
                throw new UnprocessableEntityException("Road segments exceeding 4.5 hours require a rest stop of at least 45 minutes", "REST_RULE_VIOLATION");
        }
        if (segments.Count > 0 && segments[^1].Request.PlannedArrivalAt - segments[0].Request.PlannedDepartureAt > TimeSpan.FromHours(24) &&
            !segments.Zip(segments.Skip(1), (first, second) => new
            {
                Gap = second.Request.PlannedDepartureAt - first.Request.PlannedArrivalAt,
                IsRestStop = first.Route.ToLocation.LocationType == LocationType.REST_STOP || second.Route.FromLocation.LocationType == LocationType.REST_STOP
            }).Any(item => item.Gap >= TimeSpan.FromHours(24) && item.IsRestStop))
            throw new UnprocessableEntityException("Trips over 24 hours require a mandatory 24 hour rest", "REST_RULE_VIOLATION");
        if (routeType == RouteType.BACKUP && primary is not null)
        {
            var primarySegments = primary.RoutePlanRoutes.OrderBy(item => item.SequenceNo).ToList();
            var identical = primarySegments.Count == segments.Count && primarySegments.Zip(segments, (left, right) => left.RouteId == right.Route.RouteId && left.Route.FromLocationId == right.Route.FromLocationId && left.Route.ToLocationId == right.Route.ToLocationId).All(item => item);
            if (identical)
                throw new ConflictException("Contingency route cannot be identical to the primary route. Please select alternative waypoints or border checkpoints.", "IDENTICAL_CONTINGENCY_ROUTE");
            var primaryLocations = primarySegments.SelectMany(item => new[] { item.Route.FromLocationId, item.Route.ToLocationId }).ToHashSet();
            if (!primaryLocations.Contains(segments[0].Route.FromLocationId) || !primaryLocations.Contains(segments[^1].Route.ToLocationId))
                throw new UnprocessableEntityException("The backup route must start and end on locations of the primary route (R16)", "BACKUP_NOT_ANCHORED");
        }
    }

    private static void EnsureDossierAllowsBackup(ComplianceDossier? dossier)
    {
        if (dossier is not null && dossier.Status > ComplianceDossierStatus.IN_PREPARATION)
            throw new ConflictException("A backup route plan cannot be created once the compliance dossier is past IN_PREPARATION", "DOSSIER_LOCKED");
    }

    private static void EnsureWriteRole(EmployeeRole role, RouteType type)
    {
        if (role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            return;
        if (role == EmployeeRole.LOGISTICS_MANAGER && type == RouteType.BACKUP)
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

    private static RouteType ParseRouteType(string value) => Enum.TryParse<RouteType>(value, true, out var result) && result is RouteType.PRIMARY or RouteType.BACKUP or RouteType.RETURN
        ? result
        : throw new BadRequestException("routeType must be PRIMARY, BACKUP or RETURN");

    private static TransportMode ParseTransportMode(string value) => Enum.TryParse<TransportMode>(value, true, out var result)
        ? result
        : throw new BadRequestException("transportMode is invalid");

    private static bool SupportsMode(string? supportedModes, TransportMode mode) => string.IsNullOrWhiteSpace(supportedModes) || supportedModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(item => item.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase));

    private sealed record MaterializedSegment(Request.SegmentRequest Request, Route Route);
}
