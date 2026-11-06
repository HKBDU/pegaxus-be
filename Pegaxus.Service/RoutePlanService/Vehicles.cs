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
    /// <summary>
    /// Two modes, chosen by the status of the route plan in the URL. CONFIRMED (PRIMARY, before PICKUP) changes the plan's own segments.
    /// ACTIVE (transport IN_TRANSIT) replaces a vehicle after an incident, on any unfinished segment of the transport, and needs an approved replacement cost.
    /// </summary>
    public async Task<Response.VehicleChangeResponse> ChangeVehiclesAsync(Guid accountId, Guid routePlanId, Request.ChangeVehiclesRequest request, CancellationToken cancellationToken = default)
    {
        var employee = await RequireEmployeeAsync(accountId, cancellationToken);
        EnsureWriteRole(employee.Role, RouteType.PRIMARY);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("reason is required");
        if (request.Segments.Count == 0 || request.Segments.Any(item => item.VehicleIds.Count == 0 || item.SequenceNo.HasValue == item.RoutePlanRouteId.HasValue))
            throw new BadRequestException("each segment needs at least one vehicleId and exactly one of sequenceNo or routePlanRouteId");

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
                .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == header.TransportId), x => x.TransportId, cancellationToken);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId), x => x.TransportPlanId, cancellationToken);
            // A mid-trip change can touch BACKUP and RETURN plans of the same transport plan, so every plan is locked in id order.
            var planIds = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == header.TransportPlanId).OrderBy(x => x.RoutePlanId)
                .Select(x => x.RoutePlanId).ToListAsync(cancellationToken);
            foreach (var id in planIds)
                await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == id), x => x.RoutePlanId, cancellationToken);
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
        var transport = routePlan.TransportPlan.Transport;
        var midTrip = routePlan.Status == RoutePlanStatus.ACTIVE;
        Guid? incidentId = null;
        if (midTrip)
        {
            if (transport.Status != TransportStatus.IN_TRANSIT)
                throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
            incidentId = request.IncidentReportId ?? throw new BadRequestException("An incident report is required while the route plan is active.", "INCIDENT_REQUIRED");
        }
        else
        {
            if (GetRouteType(routePlan) != RouteType.PRIMARY)
                throw new ConflictException("Only a primary route plan can have its vehicles changed here", "INVALID_ROUTE_PLAN_TYPE");
            // CONFIRMED turns ACTIVE at PICKUP, so this is the window between legal review and departure.
            if (routePlan.Status != RoutePlanStatus.CONFIRMED)
                throw new ConflictException($"Vehicles can only be changed while the route plan is confirmed or active, not {UserFacingText.Label(routePlan.Status)}", "INVALID_ROUTE_PLAN_STATUS");
            if (request.IncidentReportId.HasValue)
                throw new BadRequestException("An incident report is only used while the route plan is active.", "VALIDATION_ERROR");
        }
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);

        var pool = routePlan.TransportPlan.RoutePlans.SelectMany(plan => plan.RoutePlanRoutes).ToList();
        RoutePlanRoute Find(Request.SegmentVehiclesRequest change)
        {
            if (change.SequenceNo.HasValue && midTrip)
                throw new BadRequestException("Segments must identify an existing route segment while the route plan is active.");
            return pool.FirstOrDefault(item => change.SequenceNo.HasValue ? item.RoutePlanId == routePlan.RoutePlanId && item.SequenceNo == change.SequenceNo : item.RoutePlanRouteId == change.RoutePlanRouteId)
                ?? throw new BadRequestException("segments contains an unknown segment");
        }
        var changes = request.Segments.Select(change => (Segment: Find(change), VehicleIds: change.VehicleIds.Distinct().ToList())).ToList();
        if (changes.Select(change => change.Segment.RoutePlanRouteId).Distinct().Count() != changes.Count)
            throw new BadRequestException("segments must list each segment once");
        foreach (var (segment, _) in changes)
            EnsureChangeable(segment);
        if (changes.Any(change => change.Segment.TransportProviderId.HasValue))
            throw new UnprocessableEntityException("An outsourced segment cannot have internal vehicles", "OUTSOURCED_SEGMENT");

        var applied = ApplyReplacementToOtherSegments(changes, pool);

        var oldIds = changes.SelectMany(change => change.Segment.RoutePlanRouteVehicles.Select(link => link.VehicleId)).ToList();
        var newIds = changes.SelectMany(change => change.VehicleIds).ToHashSet();
        var removedIds = oldIds.Where(id => !newIds.Contains(id)).Distinct().ToList();
        var replaced = midTrip ? await EnsureIncidentAllowsReplacementAsync(incidentId!.Value, transport.TransportId, removedIds, cancellationToken) : new List<VehicleIncidentDetail>();

        var touchedIds = oldIds.Concat(newIds).Distinct().OrderBy(id => id).ToList();
        if (db.Database.IsNpgsql())
            foreach (var id in touchedIds)
                await db.LockRowsAsync(db.Vehicles.Where(x => x.VehicleId == id), x => x.VehicleId, cancellationToken);

        var checkedAt = UtcNowTruncated();
        foreach (var (segment, vehicleIds) in changes)
        {
            var departure = segment.PlannedDepartureAt ?? throw new ConflictException("The segment has no planned times", "SEGMENT_TIMES_MISSING");
            var arrival = segment.PlannedArrivalAt ?? throw new ConflictException("The segment has no planned times", "SEGMENT_TIMES_MISSING");
            // A segment on the road keeps its vehicle until it really arrives, even when it runs past the planned arrival.
            if (segment.ActualDepartureAt.HasValue && arrival < checkedAt)
                arrival = checkedAt;
            await ValidateVehicleAndProviderAsync(new Request.SegmentRequest
            {
                SequenceNo = segment.SequenceNo, VehicleIds = vehicleIds,
                PlannedDepartureAt = AsUtc(departure), PlannedArrivalAt = AsUtc(arrival)
            }, segment.Route, routePlan.TransportPlan, segment.RoutePlanId, cancellationToken);
        }
        var departedIds = changes.Where(change => change.Segment.ActualDepartureAt.HasValue).SelectMany(change => change.VehicleIds).ToHashSet();
        if (midTrip)
        {
            // An incident needs a vehicle now, and one that is out on another trip may not be back in time: unlike planning ahead, the schedule is no excuse.
            // Only the vehicles already travelling with this transport may be IN_USE.
            var added = changes.SelectMany(change => change.VehicleIds.Except(change.Segment.RoutePlanRouteVehicles.Select(link => link.VehicleId))).ToHashSet();
            var withThisTransport = pool.Where(segment => segment.ActualDepartureAt.HasValue && !segment.ActualArrivalAt.HasValue)
                .SelectMany(segment => segment.RoutePlanRouteVehicles.Select(link => link.VehicleId)).ToList();
            if (await db.Vehicles.AnyAsync(x => added.Contains(x.VehicleId) && x.Status == VehicleStatus.IN_USE && !withThisTransport.Contains(x.VehicleId), cancellationToken))
                throw new UnprocessableEntityException("The vehicle is not available", "VEHICLE_UNAVAILABLE");
            // A vehicle the crew has reported as unable to run is not taken out of service until the replacement cost is approved, so it must not be picked as a replacement meanwhile.
            if (await db.VehicleIncidentDetails.AnyAsync(x => added.Contains(x.VehicleId) && !x.VehicleOperable &&
                    (x.IncidentReport.Status == IncidentStatus.REPORTED || x.IncidentReport.Status == IncidentStatus.PENDING), cancellationToken))
                throw new UnprocessableEntityException("The vehicle is not available", "VEHICLE_UNAVAILABLE");
        }

        var now = new DateTime(Math.Max(UtcNowTruncated().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        var before = changes.Select(change => new { change.Segment.RoutePlanRouteId, change.Segment.SequenceNo, VehicleIds = change.Segment.RoutePlanRouteVehicles.Select(link => link.VehicleId).ToList() }).ToList();
        var note = midTrip ? "Changed during transit." : "Changed before departure.";
        foreach (var (segment, vehicleIds) in changes)
        {
            var current = segment.RoutePlanRouteVehicles.ToList();
            db.RoutePlanRouteVehicles.RemoveRange(current.Where(link => !vehicleIds.Contains(link.VehicleId)));
            foreach (var id in vehicleIds.Where(id => current.All(link => link.VehicleId != id)))
                db.RoutePlanRouteVehicles.Add(new RoutePlanRouteVehicle { RoutePlanRouteId = segment.RoutePlanRouteId, VehicleId = id, AssignedAt = now, Note = note });
            segment.UpdatedAt = now;
        }

        // A vehicle another unfinished segment of a confirmed or active plan still holds keeps its flag; only a vehicle nobody holds is released.
        // The segments of a BACKUP plan before its branch point are never driven, so they do not count.
        var changedSegmentIds = changes.Select(change => change.Segment.RoutePlanRouteId).ToList();
        var holders = await db.RoutePlanRouteVehicles.AsNoTracking()
            .Where(link => removedIds.Contains(link.VehicleId) && !changedSegmentIds.Contains(link.RoutePlanRouteId) &&
                link.RoutePlanRoute.ActualArrivalAt == null && link.RoutePlanRoute.SupersededAt == null &&
                (link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.CONFIRMED || link.RoutePlanRoute.RoutePlan.Status == RoutePlanStatus.ACTIVE))
            .Select(link => new
            {
                link.VehicleId, link.RoutePlanRoute.SequenceNo,
                Branch = link.RoutePlanRoute.RoutePlan.RoutePlanRoutes.Where(segment => segment.RouteType == RouteType.BACKUP).Select(segment => (int?)segment.SequenceNo).Min()
            }).ToListAsync(cancellationToken);
        var heldElsewhere = holders.Where(link => link.Branch is null || link.SequenceNo >= link.Branch).Select(link => link.VehicleId).ToHashSet();
        var released = new List<Guid>();
        var maintenance = new List<Guid>();
        foreach (var vehicle in await db.Vehicles.Where(x => touchedIds.Contains(x.VehicleId)).ToListAsync(cancellationToken))
        {
            if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(cancellationToken);
            if (newIds.Contains(vehicle.VehicleId))
            {
                if (departedIds.Contains(vehicle.VehicleId) && vehicle.Status is VehicleStatus.AVAILABLE or VehicleStatus.ASSIGNED)
                    { vehicle.Status = VehicleStatus.IN_USE; vehicle.UpdatedAt = now; }
                else if (vehicle.Status == VehicleStatus.AVAILABLE)
                    { vehicle.Status = VehicleStatus.ASSIGNED; vehicle.UpdatedAt = now; }
            }
            else if (!heldElsewhere.Contains(vehicle.VehicleId))
            {
                if (vehicle.Status == VehicleStatus.ASSIGNED)
                    { vehicle.Status = VehicleStatus.AVAILABLE; vehicle.UpdatedAt = now; released.Add(vehicle.VehicleId); }
                else if (midTrip && vehicle.Status == VehicleStatus.IN_USE)
                {
                    // The vehicle that broke down is left on the road, so it only returns to the pool when nothing needs fixing.
                    var detail = replaced.Single(item => item.VehicleId == vehicle.VehicleId);
                    var needsRepair = detail.RepairRequired || detail.TowingRequired || !detail.VehicleOperable;
                    vehicle.Status = needsRepair ? VehicleStatus.MAINTENANCE : VehicleStatus.AVAILABLE; vehicle.UpdatedAt = now;
                    (needsRepair ? maintenance : released).Add(vehicle.VehicleId);
                }
            }
        }

        foreach (var plan in changes.Select(change => change.Segment.RoutePlan).Append(routePlan).Distinct())
            plan.UpdatedAt = now;
        db.CatalogAuditLogs.Add(new()
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = "RoutePlan", EntityId = routePlan.RoutePlanId, ActorAccountId = accountId,
            ActionType = "ROUTE_PLAN_VEHICLES_CHANGED", Summary = request.Reason.Trim(),
            BeforeData = JsonSerializer.SerializeToDocument(new { Segments = before }),
            AfterData = JsonSerializer.SerializeToDocument(new
            {
                IncidentReportId = incidentId, AppliedSegmentIds = applied,
                Segments = changes.Select(change => new { change.Segment.RoutePlanRouteId, change.Segment.SequenceNo, change.VehicleIds }).ToList()
            }),
            CreatedAt = now
        });
        var recipients = routePlan.TransportPlan.EmployeeTransportPlans.Select(item => item.Employee)
            .Concat(changes.SelectMany(change => new[] { change.Segment.DriverEmployee, change.Segment.EscortEmployee }).OfType<Employee>());
        if (midTrip)
            recipients = recipients.Concat(await db.Employees.AsNoTracking().Active().Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER).ToListAsync(cancellationToken));
        foreach (var recipient in recipients.Where(item => item.AccountId != accountId).DistinctBy(item => item.AccountId))
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipient.AccountId, TransportId = transport.TransportId, Type = NotificationType.ROUTE,
                Priority = midTrip ? NotificationPriority.URGENT : NotificationPriority.HIGH,
                Title = "Route plan vehicles changed",
                Message = $"Vehicles of route plan {routePlan.RoutePlanCode} were changed {(midTrip ? "during the trip because of an incident" : "before departure")}. Reason: {request.Reason.Trim()}",
                ReferenceType = "RoutePlan", ReferenceId = routePlan.RoutePlanId,
                ActionUrl = NotificationLinks.RoutePlan(recipient.Role, routePlan.RoutePlanId), CreatedAt = now
            });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);
        return new Response.VehicleChangeResponse
        {
            RoutePlanId = routePlan.RoutePlanId, Status = routePlan.Status.ToString(), ReleasedVehicleIds = released, MaintenanceVehicleIds = maintenance, AppliedSegmentIds = applied, UpdatedAt = AsUtc(now)
        };
    }

    /// <summary>
    /// Carries a replacement over to the other unfinished segments of the transport plan that still hold the replaced vehicle, so a BACKUP or RETURN plan
    /// does not keep a vehicle that is gone. They join <paramref name="changes"/> and go through the same checks. A vehicle is carried over only when
    /// every segment that dropped it was given the same new vehicles; otherwise the segments must be listed one by one.
    /// </summary>
    private static List<Guid> ApplyReplacementToOtherSegments(List<(RoutePlanRoute Segment, List<Guid> VehicleIds)> changes, List<RoutePlanRoute> all)
    {
        var replacements = new Dictionary<Guid, List<Guid>>();
        var ambiguous = new HashSet<Guid>();
        foreach (var (segment, vehicleIds) in changes)
        {
            var current = segment.RoutePlanRouteVehicles.Select(link => link.VehicleId).ToList();
            var added = vehicleIds.Except(current).Order().ToList();
            foreach (var dropped in current.Except(vehicleIds))
            {
                if (replacements.TryGetValue(dropped, out var known) && !known.SequenceEqual(added))
                    ambiguous.Add(dropped);
                replacements[dropped] = added;
            }
        }
        var kept = changes.SelectMany(change => change.VehicleIds).ToHashSet();
        foreach (var id in replacements.Where(item => item.Value.Count == 0 || kept.Contains(item.Key)).Select(item => item.Key).ToList())
        {
            replacements.Remove(id);
            ambiguous.Remove(id);
        }

        var done = changes.Select(change => change.Segment.RoutePlanRouteId).ToHashSet();
        var applied = new List<Guid>();
        foreach (var segment in all.Where(item => !done.Contains(item.RoutePlanRouteId) && IsInItinerary(item)))
        {
            var current = segment.RoutePlanRouteVehicles.Select(link => link.VehicleId).ToList();
            var hit = current.Where(replacements.ContainsKey).ToList();
            if (hit.Count == 0) continue;
            if (hit.Any(ambiguous.Contains))
                throw new UnprocessableEntityException("The same vehicle is replaced by different vehicles, so list every segment that still uses it", "AMBIGUOUS_REPLACEMENT");
            changes.Add((segment, current.Except(hit).Concat(hit.SelectMany(id => replacements[id])).Distinct().ToList()));
            applied.Add(segment.RoutePlanRouteId);
        }
        return applied;
    }

    private static bool IsInItinerary(RoutePlanRoute segment) =>
        segment.RoutePlan.Status is RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE && !segment.ActualArrivalAt.HasValue && !segment.SupersededAt.HasValue &&
        segment.SequenceNo >= (TransportItinerary.BranchSequenceNo(segment.RoutePlan) ?? 0);

    private static void EnsureChangeable(RoutePlanRoute segment)
    {
        var plan = segment.RoutePlan;
        if (plan.Status is not (RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE))
            throw new ConflictException($"Vehicles can only be changed on a confirmed or active route plan, not {UserFacingText.Label(plan.Status)}", "INVALID_ROUTE_PLAN_STATUS");
        if (segment.ActualArrivalAt.HasValue)
            throw new UnprocessableEntityException("The segment is already completed", "SEGMENT_ALREADY_COMPLETED");
        if (segment.SupersededAt.HasValue || segment.SequenceNo < (TransportItinerary.BranchSequenceNo(plan) ?? 0))
            throw new UnprocessableEntityException("The segment is no longer part of the itinerary", "SEGMENT_NOT_IN_ITINERARY");
    }

    /// <summary>The cost approval is the Logistics Manager's say; the coordinator may only replace a vehicle that the incident reports, once that cost is approved.</summary>
    private async Task<List<VehicleIncidentDetail>> EnsureIncidentAllowsReplacementAsync(Guid incidentReportId, Guid transportId, List<Guid> removedIds, CancellationToken cancellationToken)
    {
        var incident = await db.IncidentReports.AsNoTracking().Where(x => x.IncidentReportId == incidentReportId)
            .Select(x => new { x.TransportId, x.Status }).SingleOrDefaultAsync(cancellationToken);
        if (incident is null || incident.TransportId != transportId)
            throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND");
        if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
            throw new UnprocessableEntityException("The incident is resolved or cancelled", "INVALID_INCIDENT_STATUS");
        var details = await db.VehicleIncidentDetails.AsNoTracking().Where(x => x.IncidentReportId == incidentReportId && removedIds.Contains(x.VehicleId)).ToListAsync(cancellationToken);
        if (removedIds.Any(id => details.All(item => item.VehicleId != id)))
            throw new UnprocessableEntityException("Only a vehicle reported in the incident can be replaced", "VEHICLE_NOT_IN_INCIDENT");
        if (!await db.EmergencyCostRequests.AnyAsync(x => x.IncidentReportId == incidentReportId &&
                x.Category == EmergencyCostCategory.REPLACEMENT_VEHICLE && x.Status == EmergencyCostStatus.APPROVED, cancellationToken))
            throw new UnprocessableEntityException("The incident has no approved replacement vehicle cost", "REPLACEMENT_COST_NOT_APPROVED");
        return details;
    }
}
