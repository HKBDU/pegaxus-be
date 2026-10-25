using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    public async Task<Response.VehicleChangeResponse> ChangeVehiclesAsync(Guid accountId, Guid routePlanId, Request.ChangeVehiclesRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        EnsureWriteRole(employee.Role, RouteType.PRIMARY);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("reason is required");
        if (request.Segments.Count == 0 || request.Segments.Any(item => item.VehicleIds.Count == 0) ||
            request.Segments.Select(item => item.SequenceNo).Distinct().Count() != request.Segments.Count)
            throw new BadRequestException("segments must list each sequenceNo once, each with at least one vehicleId");

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
                .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == header.TransportId), x => x.TransportId, cancellationToken);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId), x => x.TransportPlanId, cancellationToken);
            await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == routePlanId), x => x.RoutePlanId, cancellationToken);
        }
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken)
            ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        // Loading the plan also brings in the booking horses that the capacity check counts, which LoadRoutePlanAsync leaves out.
        await LoadPlanAsync(routePlan.TransportPlanId, cancellationToken);
        if (db.Database.IsRelational())
        {
            await db.Entry(routePlan).ReloadAsync(cancellationToken);
            await db.Entry(routePlan.TransportPlan.Transport).ReloadAsync(cancellationToken);
        }
        if (GetRouteType(routePlan) != RouteType.PRIMARY)
            throw new ConflictException("Only a PRIMARY route plan can have its vehicles changed here", "INVALID_ROUTE_PLAN_TYPE");
        // CONFIRMED turns ACTIVE at PICKUP, so this is the window between legal review and departure.
        if (routePlan.Status != RoutePlanStatus.CONFIRMED)
            throw new ConflictException($"Vehicles can only be changed while the route plan is CONFIRMED, not {routePlan.Status}", "INVALID_ROUTE_PLAN_STATUS");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);

        var segments = routePlan.RoutePlanRoutes.ToDictionary(item => item.SequenceNo);
        var changes = request.Segments.Select(change => (
            Segment: segments.GetValueOrDefault(change.SequenceNo) ?? throw new BadRequestException("segments contains an unknown sequenceNo"),
            VehicleIds: change.VehicleIds.Distinct().ToList())).ToList();
        if (changes.Any(change => change.Segment.TransportProviderId.HasValue))
            throw new UnprocessableEntityException("An outsourced segment cannot have internal vehicles", "OUTSOURCED_SEGMENT");

        var oldIds = changes.SelectMany(change => change.Segment.RoutePlanRouteVehicles.Select(link => link.VehicleId)).ToList();
        var touchedIds = oldIds.Concat(changes.SelectMany(change => change.VehicleIds)).Distinct().OrderBy(id => id).ToList();
        if (db.Database.IsNpgsql())
            foreach (var id in touchedIds)
                await db.LockRowsAsync(db.Vehicles.Where(x => x.VehicleId == id), x => x.VehicleId, cancellationToken);

        foreach (var (segment, vehicleIds) in changes)
        {
            var departure = segment.PlannedDepartureAt ?? throw new ConflictException("The segment has no planned times", "SEGMENT_TIMES_MISSING");
            var arrival = segment.PlannedArrivalAt ?? throw new ConflictException("The segment has no planned times", "SEGMENT_TIMES_MISSING");
            await ValidateVehicleAndProviderAsync(new Request.SegmentRequest
            {
                SequenceNo = segment.SequenceNo, VehicleIds = vehicleIds,
                PlannedDepartureAt = AsUtc(departure), PlannedArrivalAt = AsUtc(arrival)
            }, segment.Route, routePlan.TransportPlan, routePlan.RoutePlanId, cancellationToken);
        }

        var now = new DateTime(Math.Max(UtcNowTruncated().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        var before = changes.Select(change => new { change.Segment.SequenceNo, VehicleIds = change.Segment.RoutePlanRouteVehicles.Select(link => link.VehicleId).ToList() }).ToList();
        foreach (var (segment, vehicleIds) in changes)
        {
            var current = segment.RoutePlanRouteVehicles.ToList();
            db.RoutePlanRouteVehicles.RemoveRange(current.Where(link => !vehicleIds.Contains(link.VehicleId)));
            foreach (var id in vehicleIds.Where(id => current.All(link => link.VehicleId != id)))
                db.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle { RoutePlanRouteId = segment.RoutePlanRouteId, VehicleId = id, AssignedAt = now, Note = "Changed before departure." });
            segment.UpdatedAt = now;
        }

        var desired = changes.ToDictionary(change => change.Segment.RoutePlanRouteId, change => change.VehicleIds);
        var planVehicleIds = routePlan.RoutePlanRoutes
            .SelectMany(segment => desired.TryGetValue(segment.RoutePlanRouteId, out var ids) ? ids : segment.RoutePlanRouteVehicles.Select(link => link.VehicleId))
            .ToHashSet();
        var removedIds = oldIds.Where(id => !planVehicleIds.Contains(id)).Distinct().ToList();
        // A vehicle another confirmed or active plan still holds must keep its flag; only a vehicle nobody holds goes back to the pool.
        var heldElsewhere = await db.RoutePlanRouteVehicles.AsNoTracking()
            .Where(link => removedIds.Contains(link.VehicleId) && link.RoutePlanRoute.RoutePlanId != routePlan.RoutePlanId && link.RoutePlanRoute.ActualArrivalAt == null &&
                (link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.CONFIRMED || link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.ACTIVE))
            .Select(link => link.VehicleId).Distinct().ToListAsync(cancellationToken);
        var released = new List<Guid>();
        foreach (var vehicle in await db.Vehicles.Where(x => touchedIds.Contains(x.VehicleId)).ToListAsync(cancellationToken))
        {
            if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(cancellationToken);
            if (planVehicleIds.Contains(vehicle.VehicleId) && vehicle.Status == VehicleStatus.AVAILABLE)
                { vehicle.Status = VehicleStatus.ASSIGNED; vehicle.UpdatedAt = now; }
            else if (removedIds.Contains(vehicle.VehicleId) && vehicle.Status == VehicleStatus.ASSIGNED && !heldElsewhere.Contains(vehicle.VehicleId))
                { vehicle.Status = VehicleStatus.AVAILABLE; vehicle.UpdatedAt = now; released.Add(vehicle.VehicleId); }
        }

        routePlan.UpdatedAt = now;
        db.CatalogAuditLogs.Add(new()
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = "RoutePlan", EntityId = routePlan.RoutePlanId, ActorAccountId = accountId,
            ActionType = "ROUTE_PLAN_VEHICLES_CHANGED", Summary = request.Reason.Trim(),
            BeforeData = JsonSerializer.SerializeToDocument(before),
            AfterData = JsonSerializer.SerializeToDocument(changes.Select(change => new { change.Segment.SequenceNo, change.VehicleIds }).ToList()),
            CreatedAt = now
        });
        foreach (var recipient in routePlan.TransportPlan.EmployeeTransportPlans.Select(item => item.Employee).Where(item => item.AccountId != accountId).DistinctBy(item => item.AccountId))
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipient.AccountId, TransportId = routePlan.TransportPlan.TransportId, Type = NotificationType.ROUTE, Priority = NotificationPriority.HIGH,
                Title = "Route plan vehicles changed",
                Message = $"Vehicles of route plan {routePlan.RoutePlanCode} were changed before departure. Reason: {request.Reason.Trim()}",
                ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId,
                ActionUrl = NotificationLinks.RoutePlan(recipient.Role, routePlan.RoutePlanId), CreatedAt = now
            });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);
        return new Response.VehicleChangeResponse
        {
            RoutePlanId = routePlan.RoutePlanId, Status = routePlan.Status.ToString(), ReleasedVehicleIds = released, UpdatedAt = AsUtc(now)
        };
    }
}
