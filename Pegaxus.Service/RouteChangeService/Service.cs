using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.RouteChangeService.Validator;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service(AppDbContext db, NotificationService.INotificationService notifications, TimeProvider clock,
    RoutePlanService.IRoutePlanService routePlans) : IRouteChangeService
{
    private DateTime Now => new(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);

    public async Task<Response.Created> CreateAsync(Guid routePlanId, Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new CreateRequestValidator());
        var actor = await ActorAsync(accountId, [EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        EnsureInTransit(plan);
        await EnsureCoordinatorAsync(plan, actor, cancellationToken);
        var backup = await SegmentsAsync(routePlanId, cancellationToken);
        EnsureTypeStatus(plan, backup, "BACKUP", RoutePlanStatus.CONFIRMED);
        var primary = await PrimaryAsync(plan, cancellationToken);
        var active = await SegmentsAsync(primary.RoutePlanId, cancellationToken);
        var tail = BackupTail(backup);
        var range = Replaceable(active);
        if (range.Concat(tail).Any(x => !x.Route.DistanceKm.HasValue || x.Route.DistanceKm < 0))
            throw new ConflictException("Measured route distances are required for BR-036.", "ROUTE_DATA_INCOMPLETE");
        var comparison = Compare(active, range, tail, request.NewEstimatedArrivalAt) with {
            DelayMinutes = (decimal)(request.NewEstimatedArrivalAt - active[^1].PlannedArrivalAt).TotalMinutes };
        if (ApprovalReasons(comparison).Length == 0)
            throw new UnprocessableEntityException("The change is below the approval threshold (BR-036)", "APPROVAL_NOT_REQUIRED");
        var managers = await db.Employees.AsNoTracking().Active().Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(x => x.AccountId).ToArrayAsync(cancellationToken);
        if (managers.Length == 0) throw new ConflictException("No active Logistics Manager can review this request.", "REVIEWER_UNAVAILABLE");
        var now = Now;
        var message = FormattableString.Invariant($"{request.Reason.Trim()}\nETA: {request.NewEstimatedArrivalAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC\nEstimated extra cost: {request.EstimatedExtraCost} {request.Currency}\n{request.Note}");
        foreach (var manager in managers)
            Notify(manager, plan, "Emergency backup activation requested", message, NotificationPriority.URGENT, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(routePlanId, "BACKUP", plan.Status.ToString(), AsUtc(now));
    }

    public async Task<Response.DecisionResult> DecideAsync(Guid routePlanId, Guid accountId, Request.DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new DecisionRequestValidator());
        await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        EnsureInTransit(plan);
        CheckVersion(plan, request.LastUpdatedAt);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        var type = PlanTypeOf(segments);
        EnsureTypeStatus(plan, segments, type, type == "BACKUP" ? RoutePlanStatus.CONFIRMED : RoutePlanStatus.DRAFT);
        if (type is not ("BACKUP" or "RETURN")) throw InvalidStatus(plan);
        var transport = plan.TransportPlan.Transport;
        var dossier = transport.MainDossier;
        if (type == "RETURN" && dossier?.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException("The compliance dossier must be approved to approve a return route plan", "DOSSIER_NOT_APPROVED");
        if (type == "RETURN" && request.Decision == "APPROVE" && transport.ExtraDossier is not null)
            throw new ConflictException("An extra compliance dossier already exists for this transport", "EXTRA_DOSSIER_ALREADY_EXISTS");
        var now = NextVersion(plan);
        Response.DossierBrief? extraBrief = null;
        if (request.Decision == "APPROVE")
        {
            if (type == "RETURN")
            {
                var extra = await CreateExtraDossierAsync(plan, dossier!, accountId, request.Note, now, cancellationToken);
                extraBrief = new(extra.Dossier.ComplianceDossierId, extra.Dossier.DossierCode, extra.Dossier.DossierType.ToString(),
                    extra.Dossier.Status.ToString(), extra.InheritedRequirementCount);
                await NotifyAssignedAsync(plan, EmployeeRole.TRANSPORT_SPECIALIST, "Extra dossier ready for the return route",
                    "A supplementary dossier was created for the return route. Add the necessary requirements and review the route plan.",
                    NotificationPriority.URGENT, now, cancellationToken);
            }
            else
            {
                var primary = await PrimaryAsync(plan, cancellationToken);
                var active = await SegmentsAsync(primary.RoutePlanId, cancellationToken);
                var range = Replaceable(active);
                var tail = BackupTail(segments);
                CheckConnections(tail);
                tail = InheritPersonnel(tail, range);
                await ValidateResourcesAsync(tail, plan, active, true, cancellationToken);
                foreach (var segment in tail)
                {
                    var row = await db.RoutePlanRoutes.SingleAsync(x => x.RoutePlanRouteId == segment.RoutePlanRouteId, cancellationToken);
                    row.DriverEmployeeId = segment.Driver?.EmployeeId;
                    row.EscortEmployeeId = segment.Escort?.EmployeeId;
                    row.UpdatedAt = now;
                }
                await SupersedeAsync(range, now, cancellationToken);
                plan.Status = RoutePlanStatus.ACTIVE;
                Notify(plan.TransportPlan.Transport.Booking.Customer.AccountId, plan, "Backup route activated", request.Note, NotificationPriority.URGENT, now);
                await NotifyPrimaryDriversAsync(plan, primary.RoutePlanId, "Backup route activated", request.Note, now, cancellationToken);
            }
        }
        else if (type == "RETURN")
        {
            plan.Status = RoutePlanStatus.CANCELLED;
            plan.RouteNote = request.Note!.Trim();
        }
        plan.UpdatedAt = now;
        await NotifyAssignedAsync(plan, EmployeeRole.FLEET_ROUTE_COORDINATOR,
            request.Decision == "APPROVE" ? "Route change approved" : "Route change rejected", request.Note, NotificationPriority.HIGH, now, cancellationToken);
        Audit(plan, accountId, "ROUTE_CHANGE_" + request.Decision, request.Note, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        var branch = type == "BACKUP" ? segments.FirstOrDefault(x => x.RouteType == "BACKUP") : null;
        return new(routePlanId, type, request.Decision, plan.Status.ToString(), extraBrief,
            branch?.SequenceNo, branch?.FromLocation, AsUtc(now));
    }

    public async Task<Response.ActivationResult> ActivateReturnAsync(Guid routePlanId, Guid accountId, Request.ActivationRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new ActivationRequestValidator());
        await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        EnsureInTransit(plan);
        CheckVersion(plan, request.LastUpdatedAt);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        EnsureTypeStatus(plan, segments, "RETURN", RoutePlanStatus.CONFIRMED);
        if (plan.TransportPlan.Transport.MainDossier?.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException("The reviewed compliance dossier must be approved.", "DOSSIER_NOT_APPROVED");
        if (plan.TransportPlan.Transport.ExtraDossier?.Status != ComplianceDossierStatus.APPROVED)
            throw new UnprocessableEntityException("The extra compliance dossier must be approved before the return route can be activated", "RETURN_DOSSIER_NOT_APPROVED");
        var active = await EffectiveSegmentsAsync(plan, cancellationToken);
        var remaining = active.Where(x => !x.ActualArrivalAt.HasValue).ToList();
        EnsureUntravelled(remaining);
        if (segments[0].FromLocation.LocationId != remaining[0].FromLocation.LocationId ||
            segments[^1].ToLocation.LocationId != active[0].FromLocation.LocationId)
            throw new UnprocessableEntityException("Return route must connect the current stop to the origin.", "RETURN_NOT_ANCHORED");
        CheckConnections(segments);
        var proposed = segments.ToList();
        foreach (var change in request.Segments ?? [])
        {
            var index = proposed.FindIndex(x => x.RoutePlanRouteId == change.RoutePlanRouteId);
            if (index < 0) throw new NotFoundException("Segment not found", "SEGMENT_NOT_FOUND");
            var segment = proposed[index];
            if ((change.Outsourcing is not null || segment.Outsourcing is not null) && change.VehicleIds is { Count: > 0 })
                throw new UnprocessableEntityException("An outsourced segment cannot have internal vehicles.", "OUTSOURCED_SEGMENT");
            if (change.Outsourcing is not null)
            {
                var provider = await db.TransportProviders.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.TransportProviderId == change.Outsourcing.TransportProviderId, cancellationToken)
                    ?? throw new NotFoundException("Transport Provider not found", "TRANSPORT_PROVIDER_NOT_FOUND");
                segment = segment with { Outsourcing = new(new(provider.TransportProviderId, provider.ProviderCode,
                    provider.ProviderName, provider.Status.ToString(), Modes(provider.SupportedModes)), null, null),
                    ExecutionType = "OUTSOURCED", Vehicles = [], Driver = null, Escort = null, Note = change.Outsourcing.Note };
            }
            else if (change.VehicleIds is { } ids)
            {
                var vehicles = await db.Vehicles.AsNoTracking().Where(x => ids.Contains(x.VehicleId)).OrderBy(x => x.VehicleId).ToListAsync(cancellationToken);
                if (vehicles.Count != ids.Count) throw new NotFoundException("Vehicle not found", "VEHICLE_NOT_FOUND");
                segment = segment with { Vehicles = vehicles.Select(Vehicle).ToList() };
            }
            proposed[index] = segment;
        }
        proposed = InheritPersonnel(proposed, active);
        await ValidateResourcesAsync(proposed, plan, remaining, true, cancellationToken);
        var now = NextVersion(plan);
        foreach (var segment in proposed)
        {
            var row = await db.RoutePlanRoutes.Include(x => x.RoutePlanRouteVehicles).SingleAsync(x => x.RoutePlanRouteId == segment.RoutePlanRouteId, cancellationToken);
            row.TransportProviderId = segment.Outsourcing?.Provider.TransportProviderId;
            row.DriverEmployeeId = segment.Driver?.EmployeeId; row.EscortEmployeeId = segment.Escort?.EmployeeId;
            row.Note = segment.Note; row.UpdatedAt = now;
            var desired = segment.Vehicles.Select(x => x.VehicleId).ToArray();
            db.RoutePlanRouteVehicles.RemoveRange(row.RoutePlanRouteVehicles.Where(x => !desired.Contains(x.VehicleId)));
            foreach (var id in desired.Where(id => !row.RoutePlanRouteVehicles.Any(x => x.VehicleId == id)))
                db.RoutePlanRouteVehicles.Add(new() { RoutePlanRouteId = row.RoutePlanRouteId, VehicleId = id, AssignedAt = now });
            if (row.TransportProviderId is Guid providerId && !await db.TransportPlanProviders.AnyAsync(x =>
                    x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId, cancellationToken) &&
                !db.TransportPlanProviders.Local.Any(x => x.TransportPlanId == plan.TransportPlanId && x.TransportProviderId == providerId))
                db.TransportPlanProviders.Add(new() { TransportPlanId = plan.TransportPlanId, TransportProviderId = providerId });
        }
        foreach (var id in proposed.SelectMany(x => x.Vehicles).Select(x => x.VehicleId).Distinct())
        {
            var vehicle = await db.Vehicles.SingleAsync(x => x.VehicleId == id, cancellationToken);
            if (db.Database.IsRelational()) await db.Entry(vehicle).ReloadAsync(cancellationToken);
            if (vehicle.Status == VehicleStatus.AVAILABLE) { vehicle.Status = VehicleStatus.ASSIGNED; vehicle.UpdatedAt = now; }
        }
        await SupersedeAsync(remaining, now, cancellationToken);
        plan.Status = RoutePlanStatus.ACTIVE; plan.UpdatedAt = now;
        Audit(plan, accountId, "RETURN_ROUTE_ACTIVATED", request.Note, now);
        Notify(plan.TransportPlan.Transport.Booking.Customer.AccountId, plan, "Return route activated", request.Note, NotificationPriority.URGENT, now);
        await NotifyAssignedAsync(plan, EmployeeRole.FLEET_ROUTE_COORDINATOR, "Return route activated", request.Note, NotificationPriority.HIGH, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var detail = await routePlans.GetByIdAsync(accountId, routePlanId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(routePlanId, "RETURN", "ACTIVE", detail.Segments, AsUtc(now));
    }

    private static List<Response.Segment> BackupTail(List<Response.Segment> backup)
    {
        if (backup.Count == 0) throw new BadRequestException("Backup itinerary is empty.");
        var branch = backup.Where(x => x.RouteType == "BACKUP").Select(x => x.SequenceNo).DefaultIfEmpty(int.MaxValue).Min();
        return backup.Where(x => x.SequenceNo >= branch).ToList();
    }

    private static List<Response.Segment> Replaceable(List<Response.Segment> active)
    {
        var range = active.Where(x => !x.ActualDepartureAt.HasValue).ToList();
        if (range.Count == 0) throw new ConflictException("Only untravelled segments can be replaced.", "SEGMENT_LOCKED");
        return range;
    }

    private static void EnsureUntravelled(List<Response.Segment> segments)
    {
        if (segments.Count == 0 || segments.Any(x => x.ActualDepartureAt.HasValue || x.ActualArrivalAt.HasValue))
            throw new ConflictException("Only untravelled segments can be replaced.", "SEGMENT_LOCKED");
    }

    private static List<Response.Segment> InheritPersonnel(List<Response.Segment> segments, List<Response.Segment> primary) =>
        segments.Select((x, i) =>
        {
            var crew = primary[Math.Min(i, primary.Count - 1)];
            if (crew.Driver is null || crew.Escort is null)
                crew = primary.LastOrDefault(p => p.Driver is not null && p.Escort is not null) ?? crew;
            return x.ExecutionType == "INTERNAL" ? x with { Driver = crew.Driver, Escort = crew.Escort }
                : x with { Driver = null, Escort = null };
        }).ToList();

    private static void CheckConnections(List<Response.Segment> segments)
    {
        if (segments.Any(x => x.PlannedArrivalAt <= x.PlannedDepartureAt))
            throw new UnprocessableEntityException("Invalid segment schedule.", "INVALID_SEGMENT_SCHEDULE");
        for (var i = 1; i < segments.Count; i++)
            if (segments[i - 1].ToLocation.LocationId != segments[i].FromLocation.LocationId ||
                segments[i].PlannedDepartureAt < (segments[i - 1].ActualArrivalAt ?? segments[i - 1].PlannedArrivalAt))
                throw new UnprocessableEntityException("Segments must connect without overlapping schedules.", "SEGMENT_NOT_CONTINUOUS");
    }

    private async Task SupersedeAsync(List<Response.Segment> segments, DateTime now, CancellationToken token)
    {
        var ids = segments.Select(x => x.RoutePlanRouteId).ToArray();
        var rows = await db.RoutePlanRoutes.Where(x => ids.Contains(x.RoutePlanRouteId)).ToListAsync(token);
        foreach (var row in rows) { row.SupersededAt = now; row.UpdatedAt = now; }
    }

    private static void EnsureInTransit(RoutePlan plan)
    {
        if (plan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
    }

    private static void EnsureTypeStatus(RoutePlan plan, List<Response.Segment> segments, string type, RoutePlanStatus status)
    {
        if (plan.Status != status || segments.Count == 0 || PlanTypeOf(segments) != type) throw InvalidStatus(plan);
    }

    private static string PlanTypeOf(List<Response.Segment> segments) =>
        segments.Any(x => x.RouteType == "BACKUP") ? "BACKUP" : segments.Count > 0 && segments.All(x => x.RouteType == "RETURN") ? "RETURN" : "PRIMARY";

    private static ConflictException InvalidStatus(RoutePlan plan) =>
        new($"This action cannot be performed while the route plan is in {plan.Status} status", "INVALID_ROUTE_PLAN_STATUS");

    private static void CheckVersion(RoutePlan plan, DateTimeOffset version)
    {
        if (plan.UpdatedAt != Truncate(version)) throw new ConflictException("The route plan was just modified. Please reload the page", "ROUTE_PLAN_MODIFIED");
    }

    private DateTime NextVersion(RoutePlan plan) => new(Math.Max(Now.Ticks, plan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
}
