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

public sealed partial class Service(AppDbContext db, NotificationService.IService notifications, TimeProvider clock,
    RoutePlanService.IService routePlans) : IService
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
        var range = ReplacementRange(active, backup);
        if (range.Concat(backup).Any(x => !x.Route.DistanceKm.HasValue || x.Route.DistanceKm < 0))
            throw new ConflictException("Measured route distances are required for BR-036.", "ROUTE_DATA_INCOMPLETE");
        var comparison = Compare(active, range, backup, request.NewEstimatedArrivalAt) with {
            DelayMinutes = (decimal)(request.NewEstimatedArrivalAt - active[^1].PlannedArrivalAt).TotalMinutes };
        if (ApprovalReasons(comparison).Length == 0)
            throw new UnprocessableEntityException("The change is below the approval threshold (BR-036)", "APPROVAL_NOT_REQUIRED");
        var managers = await db.Employees.AsNoTracking().Active().Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(x => x.AccountId).ToArrayAsync(cancellationToken);
        if (managers.Length == 0) throw new ConflictException("No active Logistics Manager can review this request.", "REVIEWER_UNAVAILABLE");
        var now = Now;
        var message = $"{request.Reason.Trim()}\nETA: {request.NewEstimatedArrivalAt:O}\nEstimated extra cost: {request.EstimatedExtraCost} {request.Currency}\n{request.Note}";
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
        var type = segments.Count > 0 ? segments[0].RouteType : "";
        EnsureTypeStatus(plan, segments, type, type == "BACKUP" ? RoutePlanStatus.CONFIRMED : RoutePlanStatus.DRAFT);
        if (type is not ("BACKUP" or "RETURN")) throw InvalidStatus(plan);
        var dossier = plan.TransportPlan.Transport.ComplianceDossier;
        if (type == "RETURN" && dossier?.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException("The compliance dossier must be approved to approve a return route plan", "DOSSIER_NOT_APPROVED");
        var now = NextVersion(plan);
        if (request.Decision == "APPROVE")
        {
            if (type == "RETURN")
            {
                dossier!.Status = ComplianceDossierStatus.DRAFT;
                dossier.ApprovedAt = null;
                dossier.UpdatedAt = now;
                db.AuditDossiers.Add(new() { AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId,
                    ActorAccountId = accountId, ActionType = "RETURN_DOSSIER_REOPENED", TargetEntity = "RoutePlan", TargetId = routePlanId,
                    Summary = request.Note ?? "Return route approved; dossier reopened for the assigned Transport Specialist.",
                    Details = JsonSerializer.SerializeToDocument(new { before = "APPROVED", after = "DRAFT" }),
                    Visibility = AuditVisibility.Internal, CreatedAt = now });
                await NotifyAssignedAsync(plan, EmployeeRole.TRANSPORT_SPECIALIST, "Return dossier reopened", request.Note, NotificationPriority.HIGH, now, cancellationToken);
            }
            else
            {
                var primary = await PrimaryAsync(plan, cancellationToken);
                var active = await SegmentsAsync(primary.RoutePlanId, cancellationToken);
                var range = ReplacementRange(active, segments);
                EnsureUntravelled(range);
                var effective = active.TakeWhile(x => x.RoutePlanRouteId != range[0].RoutePlanRouteId).Concat(segments)
                    .Concat(active.SkipWhile(x => x.RoutePlanRouteId != range[^1].RoutePlanRouteId).Skip(1)).ToList();
                CheckConnections(effective);
                segments = InheritPersonnel(segments, range);
                await ValidateResourcesAsync(segments, plan, range, true, cancellationToken);
                foreach (var segment in segments)
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
        return new(routePlanId, type, request.Decision, plan.Status.ToString(),
            type == "RETURN" ? new(dossier!.ComplianceDossierId, dossier.Status.ToString()) : null, AsUtc(now));
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
        if (plan.TransportPlan.Transport.ComplianceDossier?.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException("The reviewed compliance dossier must be approved.", "DOSSIER_NOT_APPROVED");
        var primary = await PrimaryAsync(plan, cancellationToken);
        var active = await SegmentsAsync(primary.RoutePlanId, cancellationToken);
        var alternateIds = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Status == RoutePlanStatus.ACTIVE && x.RoutePlanId != primary.RoutePlanId)
            .Select(x => x.RoutePlanId).ToArrayAsync(cancellationToken);
        foreach (var alternateId in alternateIds) active.AddRange(await SegmentsAsync(alternateId, cancellationToken));
        active = active.OrderBy(x => x.PlannedDepartureAt).ThenBy(x => x.SequenceNo).ToList();
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

    public async Task<Response.DriverNoticeResult> NotifyDriversAsync(Guid routePlanId, Guid accountId, Request.DriverNoticeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Message?.Length > 4000) throw new BadRequestException("Message must not exceed 4000 characters.");
        var actor = await ActorAsync(accountId, [EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        await EnsureCoordinatorAsync(plan, actor, cancellationToken);
        var segments = await SegmentsAsync(routePlanId, cancellationToken);
        if (plan.Status is not (RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE)) throw InvalidStatus(plan);
        EnsureTypeStatus(plan, segments, "RETURN", plan.Status);
        var primary = await PrimaryAsync(plan, cancellationToken);
        var now = Now;
        var drivers = await NotifyPrimaryDriversAsync(plan, primary.RoutePlanId, "Begin return journey", request.Message, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(routePlanId, drivers.Select(x => Employee(x)!).ToArray(), AsUtc(now));
    }

    private async Task EnsureCoordinatorAsync(RoutePlan plan, Employee actor, CancellationToken token)
    {
        if (!await db.EmployeeTransportPlans.AnyAsync(x => x.TransportPlanId == plan.TransportPlanId &&
            x.EmployeeId == actor.EmployeeId && x.Responsibility == "FLEET_ROUTE_COORDINATOR", token))
            throw new ForbiddenException("Only the Route Coordinator assigned to this Transport Plan can perform this action.");
    }

    private async Task<RoutePlan> PrimaryAsync(RoutePlan plan, CancellationToken token)
    {
        var primaryIds = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Status == RoutePlanStatus.ACTIVE && x.RoutePlanRoutes.Any() &&
            x.RoutePlanRoutes.All(s => s.RouteType == RouteType.PRIMARY)).Select(x => x.RoutePlanId).ToArrayAsync(token);
        if (primaryIds.Length != 1) throw new ConflictException("One active PRIMARY route is required.", "PRIMARY_ROUTE_PLAN_REQUIRED");
        return await LockPlanAsync(primaryIds[0], token);
    }

    private static List<Response.Segment> ReplacementRange(List<Response.Segment> primary, List<Response.Segment> backup)
    {
        if (backup.Count == 0) throw new BadRequestException("Backup itinerary is empty.");
        var start = primary.FindIndex(x => x.FromLocation.LocationId == backup[0].FromLocation.LocationId);
        var end = primary.FindLastIndex(x => x.ToLocation.LocationId == backup[^1].ToLocation.LocationId);
        if (start < 0 || end < start) throw new ConflictException("Backup overlaps an active replacement or is not anchored to PRIMARY.", "BACKUP_RANGE_CONFLICT");
        var range = primary.GetRange(start, end - start + 1);
        if (range.Zip(range.Skip(1), (a, b) => a.ToLocation.LocationId == b.FromLocation.LocationId).Any(x => !x))
            throw new ConflictException("Backup overlaps an active replacement.", "BACKUP_RANGE_CONFLICT");
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

    private async Task NotifyAssignedAsync(RoutePlan plan, EmployeeRole role, string title, string? message,
        NotificationPriority priority, DateTime now, CancellationToken token)
    {
        var recipients = await db.EmployeeTransportPlans.AsNoTracking().Where(x => x.TransportPlanId == plan.TransportPlanId &&
            x.Employee.Role == role && x.Employee.IsActive && !x.Employee.IsDeleted &&
            !x.Employee.Account.IsDeleted && x.Employee.Account.Status == AccountStatus.ACTIVE)
            .Select(x => x.Employee.AccountId).Distinct().ToArrayAsync(token);
        foreach (var recipient in recipients) Notify(recipient, plan, title, message, priority, now);
    }

    private async Task<List<Employee>> NotifyPrimaryDriversAsync(RoutePlan plan, Guid primaryId, string title, string? message, DateTime now, CancellationToken token)
    {
        var driverIds = db.RoutePlanRoutes.Where(x => x.RoutePlanId == primaryId && x.DriverEmployeeId.HasValue).Select(x => x.DriverEmployeeId!.Value);
        var drivers = await db.Employees.AsNoTracking().Active().Where(x => driverIds.Contains(x.EmployeeId) && x.Role == EmployeeRole.DRIVER).OrderBy(x => x.EmployeeCode).ToListAsync(token);
        foreach (var driver in drivers) Notify(driver.AccountId, plan, title, message, NotificationPriority.URGENT, now);
        return drivers;
    }

    private void Notify(Guid accountId, RoutePlan plan, string title, string? message, NotificationPriority priority, DateTime now) =>
        notifications.Queue(new() { AccountId = accountId, TransportId = plan.TransportPlan.TransportId, Type = NotificationType.ROUTE,
            Priority = priority, Title = title, Message = $"{plan.RoutePlanCode}: {message}", ReferenceType = "RoutePlan",
            ReferenceId = plan.RoutePlanId, ActionUrl = $"/admin/route-plans/{plan.RoutePlanId}", CreatedAt = now });

    private void Audit(RoutePlan plan, Guid accountId, string action, string? note, DateTime now) =>
        db.CatalogAuditLogs.Add(new() { CatalogAuditLogId = Guid.NewGuid(), EntityType = "RoutePlan", EntityId = plan.RoutePlanId,
            ActorAccountId = accountId, ActionType = action, Summary = note, BeforeData = JsonDocument.Parse("{}"),
            AfterData = JsonSerializer.SerializeToDocument(new { status = plan.Status.ToString() }), CreatedAt = now });

    private static void EnsureInTransit(RoutePlan plan)
    {
        if (plan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
    }

    private static void EnsureTypeStatus(RoutePlan plan, List<Response.Segment> segments, string type, RoutePlanStatus status)
    {
        if (plan.Status != status || segments.Count == 0 || segments.Any(x => x.RouteType != type)) throw InvalidStatus(plan);
    }

    private static ConflictException InvalidStatus(RoutePlan plan) =>
        new($"This action cannot be performed while the route plan is in {plan.Status} status", "INVALID_ROUTE_PLAN_STATUS");
    private static void CheckVersion(RoutePlan plan, DateTimeOffset version)
    {
        if (plan.UpdatedAt != Truncate(version)) throw new ConflictException("The route plan was just modified. Please reload the page", "ROUTE_PLAN_MODIFIED");
    }
    private DateTime NextVersion(RoutePlan plan) => new(Math.Max(Now.Ticks, plan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);

    private async Task<Employee> ActorAsync(Guid accountId, EmployeeRole[] roles, CancellationToken token)
    {
        var actor = await db.Employees.AsNoTracking().Include(x => x.Account).SingleOrDefaultAsync(x => x.AccountId == accountId, token);
        if (actor is null || !actor.IsActive || actor.IsDeleted || actor.Account.IsDeleted || actor.Account.Status != AccountStatus.ACTIVE ||
            actor.Account.AccountType != AccountType.EMPLOYEE || !roles.Contains(actor.Role))
            throw new ForbiddenException("You do not have permission to perform this action.");
        return actor;
    }

    private async Task<RoutePlan> LockPlanAsync(Guid routePlanId, CancellationToken token)
    {
        var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
            .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("Route plan not found.", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsRelational())
        {
            // Updating the key to itself holds a row lock until the transaction ends.
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == header.TransportId), x => x.TransportId, token);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId), x => x.TransportPlanId, token);
            await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == routePlanId), x => x.RoutePlanId, token);
            await LockSegmentsAsync(routePlanId, token);
        }
        var plan = await db.RoutePlans.Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.Booking).ThenInclude(x => x.Customer).ThenInclude(x => x.Account)
            .Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.ComplianceDossier)
            .SingleAsync(x => x.RoutePlanId == routePlanId, token);
        await db.Entry(plan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan.Transport).ReloadAsync(token);
        var dossier = plan.TransportPlan.Transport.ComplianceDossier;
        if (dossier is not null && db.Database.IsRelational())
        {
            await db.LockRowsAsync(db.ComplianceDossiers.Where(x => x.ComplianceDossierId == dossier.ComplianceDossierId), x => x.ComplianceDossierId, token);
            await db.Entry(dossier).ReloadAsync(token);
        }
        return plan;
    }

    private async Task LockSegmentsAsync(Guid routePlanId, CancellationToken token)
    {
        // Acquire segment locks in the same order for primary and backup routes to avoid deadlocks.
        var ids = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == routePlanId)
            .OrderBy(x => x.RoutePlanRouteId).Select(x => x.RoutePlanRouteId).ToArrayAsync(token);
        foreach (var id in ids)
            await db.LockRowsAsync(db.RoutePlanRoutes.Where(x => x.RoutePlanRouteId == id), x => x.RoutePlanRouteId, token);
    }

    private IQueryable<RoutePlanRoute> SegmentQuery() => db.RoutePlanRoutes.AsNoTracking()
        .Include(x => x.Route).ThenInclude(x => x.FromLocation).Include(x => x.Route).ThenInclude(x => x.ToLocation)
        .Include(x => x.DriverEmployee).Include(x => x.EscortEmployee).Include(x => x.TransportProvider)
        .Include(x => x.RoutePlanRouteVehicles).ThenInclude(x => x.Vehicle);

    private async Task<List<Response.Segment>> SegmentsAsync(Guid planId, CancellationToken token)
    {
        var rows = await SegmentQuery().Where(x => x.RoutePlanId == planId && x.SupersededAt == null).OrderBy(x => x.SequenceNo).ToListAsync(token);
        return rows.Select((x, i) => Segment(x, i + 1 < rows.Count ? rows[i + 1].PlannedDepartureAt : null)).ToList();
    }

    private static Response.EmployeeBrief? Employee(Employee? x) => x is null ? null : new(x.EmployeeId, x.EmployeeCode, x.FullName, x.Role.ToString());
    private static Response.LocationBrief Location(Location x) => new(x.LocationId, x.LocationName, x.LocationType?.ToString(), x.City, x.Country, x.Latitude, x.Longitude);
    private static Response.VehicleBrief Vehicle(Vehicle x) => new(x.VehicleId, x.VehicleCode, x.VehicleName, x.LicensePlate, x.TransportMode.ToString(), x.CapacityHorses);
    private static string[] Modes(string? modes) => (modes ?? "").Split([',', ';', '|', ' ', '[', ']', '"'], StringSplitOptions.RemoveEmptyEntries).Distinct().Order().ToArray();
    private static Response.Segment Segment(RoutePlanRoute x, DateTime? nextDeparture)
    {
        if (x.PlannedArrivalAt is null || x.PlannedDepartureAt is null || x.Route.DistanceKm < 0)
            throw new ConflictException("A valid segment schedule and nonnegative route distance are required.", "ROUTE_DATA_INCOMPLETE");
        return new(x.RoutePlanRouteId, x.SequenceNo, x.RouteType.ToString(),
            new(x.RouteId, x.Route.RouteCode, x.Route.RouteName, x.Route.TransportMode.ToString(), x.Route.DistanceKm, x.Route.EstimatedDurationMinutes),
            Location(x.Route.FromLocation), Location(x.Route.ToLocation), AsUtc(x.PlannedDepartureAt.Value), AsUtc(x.PlannedArrivalAt.Value),
            AsUtc(x.ActualDepartureAt), AsUtc(x.ActualArrivalAt), x.ActualArrivalAt.HasValue ? "COMPLETED" : x.ActualDepartureAt.HasValue ? "IN_PROGRESS" : "PLANNED",
            nextDeparture.HasValue ? (decimal)(nextDeparture.Value - x.PlannedArrivalAt.Value).TotalMinutes : null,
            x.TransportProviderId.HasValue ? "OUTSOURCED" : "INTERNAL", x.RoutePlanRouteVehicles.OrderBy(v => v.VehicleId).Select(v => Vehicle(v.Vehicle)).ToList(),
            x.TransportProvider is { } p ? new(new(p.TransportProviderId, p.ProviderCode, p.ProviderName, p.Status.ToString(), Modes(p.SupportedModes)), x.ServiceType, x.ProviderReference) : null,
            Employee(x.DriverEmployee), Employee(x.EscortEmployee), x.Note);
    }

    private static Response.Comparison Compare(List<Response.Segment> active, List<Response.Segment> current, List<Response.Segment> proposed, DateTimeOffset eta)
    {
        static Guid[] Bcps(List<Response.Segment> list) => list.SelectMany(x => new[] { x.FromLocation, x.ToLocation })
            .Where(x => x.LocationType == "BORDER_GATE").Select(x => x.LocationId).Distinct().ToArray();
        Dictionary<Guid, int> Rest(List<Response.Segment> list)
        {
            var rest = list.Where(x => x.DwellAfterMinutes >= 1440).GroupBy(x => x.ToLocation.LocationId)
                .ToDictionary(x => x.Key, x => x.Sum(s => (int)(s.DwellAfterMinutes!.Value / 1440)));
            if (active.LastOrDefault(x => x.ActualArrivalAt.HasValue)?.ActualArrivalAt is { } arrival)
            {
                var days = (int)((list[0].PlannedDepartureAt - arrival).TotalMinutes / 1440);
                if (days > 0) rest[list[0].FromLocation.LocationId] = rest.GetValueOrDefault(list[0].FromLocation.LocationId) + days;
            }
            return rest;
        }
        var oldRest = Rest(current);
        return new(proposed.Sum(x => x.Route.DistanceKm!.Value) - current.Sum(x => x.Route.DistanceKm!.Value),
            (decimal)(eta - current[^1].PlannedArrivalAt).TotalMinutes, !Bcps(current).SequenceEqual(Bcps(proposed)),
            Rest(proposed).Any(x => x.Value > oldRest.GetValueOrDefault(x.Key)),
            current.SelectMany(x => new[] { x.FromLocation, x.ToLocation }).FirstOrDefault(x => x.LocationType == "BORDER_GATE"),
            proposed.SelectMany(x => new[] { x.FromLocation, x.ToLocation }).FirstOrDefault(x => x.LocationType == "BORDER_GATE"), current[^1].PlannedArrivalAt, eta);
    }

    private static string[] ApprovalReasons(Response.Comparison comparison)
    {
        var reasons = new List<string>();
        if (comparison.BcpChanged) reasons.Add("BCP_CHANGED");
        if (comparison.Additional24hRest) reasons.Add("ADDITIONAL_24H_REST");
        if (Math.Abs(comparison.DistanceDeltaKm) >= 50) reasons.Add("DEVIATION_OVER_50KM");
        if (comparison.DelayMinutes >= 60) reasons.Add("DELAY_OVER_1H");
        return reasons.ToArray();
    }

}
