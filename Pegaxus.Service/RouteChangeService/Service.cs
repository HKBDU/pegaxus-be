using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;
using Pegaxus.Service.RouteChangeService.Validator;

namespace Pegaxus.Service.RouteChangeService;

public sealed partial class Service(AppDbContext db, NotificationService.IService notifications, TimeProvider clock) : IService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateTime Now => new(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTime DbTime(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions)!;
    private static void Validate<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (!result.IsValid) throw new BadRequestException(string.Join(" ", result.Errors.Select(x => x.ErrorMessage)));
    }

    public async Task<Response.Created> CreateAsync(Guid routePlanId, Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new CreateRequestValidator());
        var actor = await ActorAsync(accountId, [EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var plan = await LockPlanAsync(routePlanId, cancellationToken);
        if (request.ClientRequestId is { } clientId)
        {
            var existing = await db.RouteChangeRequests.AsNoTracking().SingleOrDefaultAsync(x =>
                x.RoutePlanId == routePlanId && x.ClientRequestId == clientId, cancellationToken);
            if (existing is not null)
            {
                if (existing.RequestedByEmployeeId != actor.EmployeeId || !JsonEquivalent(existing.SubmittedRequest, Json(request)))
                    throw new ConflictException("clientRequestId was already used with a different request.", "IDEMPOTENCY_KEY_REUSED");
                return new(existing.RouteChangeRequestId, routePlanId, "PENDING", true, existing.ApprovalReasons, Utc(existing.CreatedAt));
            }
        }
        EnsureInTransit(plan);
        if (await db.RouteChangeRequests.AnyAsync(x => x.RoutePlanId == routePlanId && x.Status == "PENDING", cancellationToken))
            throw new ConflictException("There is already a pending change request for this route plan.", "CHANGE_REQUEST_PENDING_EXISTS");

        var active = await SegmentsAsync(routePlanId, cancellationToken);
        var remaining = Remaining(active);
        var proposed = request.BackupRoutePlanId is { } backupId
            ? BackupRemainder(await BackupAsync(backupId, plan, cancellationToken), remaining, Utc(DbTime(request.NewEstimatedArrivalAt)))
            : await ResolveAsync(request.ReplacementSegments!, remaining, cancellationToken);
        var eta = Utc(DbTime(request.NewEstimatedArrivalAt));
        CheckContinuity(active, proposed, eta, false);
        if (proposed[0].PlannedDepartureAt < Utc(Now))
            throw new BadRequestException("Proposed departure must not be in the past.");
        await ValidateResourcesAsync(proposed, plan, remaining, false, cancellationToken);
        var comparison = Compare(active, remaining, proposed, eta);
        var reasons = ApprovalReasons(comparison);
        if (reasons.Length == 0)
            throw new UnprocessableEntityException("The change is below the BR-036 thresholds and does not require approval.", "APPROVAL_NOT_REQUIRED");

        var now = Now;
        var change = new RouteChangeRequest
        {
            RouteChangeRequestId = Guid.NewGuid(), RoutePlanId = routePlanId, BackupRoutePlanId = request.BackupRoutePlanId,
            ClientRequestId = request.ClientRequestId, SubmittedRequest = Json(request),
            Reason = request.Reason.Trim(), Note = request.Note?.Trim(), NewEstimatedArrivalAt = DbTime(request.NewEstimatedArrivalAt),
            EstimatedExtraCost = request.EstimatedExtraCost, Currency = request.Currency, CurrentSegments = Json(remaining),
            ProposedSegments = Json(proposed), Comparison = Json(comparison), ApprovalReasons = reasons,
            RequestedByEmployeeId = actor.EmployeeId, CreatedAt = now, UpdatedAt = now
        };
        db.RouteChangeRequests.Add(change);
        var managers = await db.Employees.AsNoTracking().Include(x => x.Account)
            .Where(x => x.Role == EmployeeRole.LOGISTICS_MANAGER && x.IsActive && !x.IsDeleted &&
                x.Account.Status == AccountStatus.ACTIVE && !x.Account.IsDeleted).ToListAsync(cancellationToken);
        if (managers.Count == 0) throw new ConflictException("No active Logistics Manager can review this request.", "REVIEWER_UNAVAILABLE");
        foreach (var manager in managers)
            Notify(manager.Account, plan.TransportPlan.TransportId, change, "Emergency route change requested", change.Reason, now);
        Audit(change, accountId, "IN_TRANSIT_ROUTE_CHANGE_REQUESTED", new { }, new { change.ApprovalReasons, comparison, proposed }, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return new(change.RouteChangeRequestId, routePlanId, change.Status, true, reasons, Utc(now));
    }

    public async Task<Response.ListResponse> ListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new ListRequestValidator());
        await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        var query = db.RouteChangeRequests.AsNoTracking();
        if (request.Status is not null) query = query.Where(x => x.Status == request.Status);
        if (request.TransportPlanId is { } planId) query = query.Where(x => x.RoutePlan.TransportPlanId == planId);
        var count = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy == "createdAt:asc" ? query.OrderBy(x => x.CreatedAt) : query.OrderByDescending(x => x.CreatedAt);
        var items = await sorted.ThenBy(x => x.RouteChangeRequestId).Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize)
            .Include(x => x.RoutePlan).ThenInclude(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.Booking)
            .ToListAsync(cancellationToken);
        return new(items.Select(x => new Response.ListItem(x.RouteChangeRequestId, x.RoutePlanId, x.RoutePlan.TransportPlanId,
            x.RoutePlan.TransportPlan.PlanCode, x.RoutePlan.TransportPlan.Transport.Booking.BookingCode, x.Status, x.Reason,
            x.ApprovalReasons, Utc(x.NewEstimatedArrivalAt), Utc(x.CreatedAt))).ToList(),
            request.PageIndex, request.PageSize, count, (int)Math.Ceiling(count / (double)request.PageSize));
    }

    public async Task<Response.Detail> GetAsync(Guid changeRequestId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR], cancellationToken);
        var change = await db.RouteChangeRequests.AsNoTracking().Include(x => x.RequestedByEmployee).Include(x => x.RoutePlan)
            .SingleOrDefaultAsync(x => x.RouteChangeRequestId == changeRequestId, cancellationToken)
            ?? throw new NotFoundException("Route change request not found.", "CHANGE_REQUEST_NOT_FOUND");
        return new(changeRequestId, change.RoutePlanId, change.RoutePlan.TransportPlanId, change.Status, change.Reason,
            Employee(change.RequestedByEmployee)!, Read<List<Response.Segment>>(change.CurrentSegments), Read<List<Response.Segment>>(change.ProposedSegments),
            change.BackupRoutePlanId, Read<Response.Comparison>(change.Comparison), change.ApprovalReasons, change.EstimatedExtraCost,
            change.Currency, change.Note, change.DecidedAt is { } decidedAt ? new(change.Status, change.DecidedByEmployeeId!.Value, change.DecisionNote, Utc(decidedAt)) : null,
            Utc(change.CreatedAt), Utc(change.UpdatedAt));
    }

    public async Task<Response.DecisionResult> DecideAsync(Guid changeRequestId, Guid accountId, Request.DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, new DecisionRequestValidator());
        var actor = await ActorAsync(accountId, [EmployeeRole.LOGISTICS_MANAGER], cancellationToken);
        var header = await db.RouteChangeRequests.AsNoTracking().SingleOrDefaultAsync(x => x.RouteChangeRequestId == changeRequestId, cancellationToken)
            ?? throw new NotFoundException("Route change request not found.", "CHANGE_REQUEST_NOT_FOUND");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var plan = await LockPlanAsync(header.RoutePlanId, cancellationToken);
        if (db.Database.IsRelational())
            await db.RouteChangeRequests.Where(x => x.RouteChangeRequestId == changeRequestId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RouteChangeRequestId, x => x.RouteChangeRequestId), cancellationToken);
        var change = await db.RouteChangeRequests.SingleAsync(x => x.RouteChangeRequestId == changeRequestId, cancellationToken);
        await db.Entry(change).ReloadAsync(cancellationToken);
        var decisionRequest = Json(new { actor.EmployeeId, request.LastUpdatedAt, request.Decision, Note = request.Note?.Trim(), request.ReturnSegments });
        if (change.Status != "PENDING")
        {
            // PostgreSQL jsonb changes key order; compare deserialized values rather than serialized text.
            if (change.DecisionRequest is not null && JsonEquivalent(change.DecisionRequest, decisionRequest))
                return Read<Response.DecisionResult>(change.DecisionResponse!);
            throw new ConflictException("This change request has already been decided.", "CHANGE_REQUEST_ALREADY_DECIDED");
        }
        if (DbTime(request.LastUpdatedAt) != change.UpdatedAt)
            throw new ConflictException("The change request was just modified. Please reload the page.", "CHANGE_REQUEST_MODIFIED");
        EnsureInTransit(plan);
        var now = new DateTime(Math.Max(Now.Ticks, change.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        List<Response.Segment> proposed = [];
        List<Response.Segment> remaining = [];
        Guid[] newIds = [];
        string[] notified;
        var isReturn = request.Decision == "EMERGENCY_RETURN";
        if (request.Decision != "REJECT")
        {
            var active = await SegmentsAsync(plan.RoutePlanId, cancellationToken);
            remaining = Remaining(active);
            if (!JsonEquivalent(Json(remaining), change.CurrentSegments))
                throw new ConflictException("The current route or its progress changed. Submit a fresh proposal.", "ROUTE_PROGRESS_CHANGED");
            proposed = isReturn ? await ReturnAsync(request, active, remaining, plan, cancellationToken)
                : Read<List<Response.Segment>>(change.ProposedSegments);
            if (!isReturn && change.BackupRoutePlanId is { } backupId)
            {
                var backup = BackupRemainder(await BackupAsync(backupId, plan, cancellationToken), remaining, Utc(change.NewEstimatedArrivalAt));
                if (!JsonEquivalent(Json(backup), change.ProposedSegments))
                    throw new ConflictException("The backup changed since submission.", "BACKUP_MODIFIED");
            }
            var eta = isReturn ? proposed[^1].PlannedArrivalAt : Utc(change.NewEstimatedArrivalAt);
            CheckContinuity(active, proposed, eta, isReturn);
            if (proposed[0].PlannedDepartureAt < Utc(Now))
                throw new ConflictException("The proposed departure is in the past. Submit an updated schedule.", "PROPOSAL_EXPIRED");
            await ValidateResourcesAsync(proposed, plan, remaining, true, cancellationToken);
            newIds = await ApplyAsync(plan, change, remaining, proposed, isReturn, now, cancellationToken);
            plan.TransportPlan.PlannedEndAt = DbTime(eta);
            plan.TransportPlan.UpdatedAt = now;
            plan.UpdatedAt = now;
            RecordIncident(plan, change, actor, remaining[0].FromLocation.LocationId, isReturn, request.Note, now);
            notified = await NotifyDecisionAsync(plan, change, proposed, remaining, isReturn, now, cancellationToken);
        }
        else
        {
            var requester = await db.Employees.AsNoTracking().Include(x => x.Account)
                .SingleAsync(x => x.EmployeeId == change.RequestedByEmployeeId, cancellationToken);
            Notify(requester.Account, plan.TransportPlan.TransportId, change, "Route change rejected", request.Note!.Trim(), now);
            notified = ["ROUTE_COORDINATOR"];
        }
        change.Status = request.Decision == "APPROVE" ? "APPROVED" : request.Decision == "REJECT" ? "REJECTED" : "EMERGENCY_RETURN";
        change.DecidedByEmployeeId = actor.EmployeeId;
        change.DecisionNote = request.Note?.Trim();
        change.DecidedAt = now;
        change.UpdatedAt = now;
        change.DecisionRequest = decisionRequest;
        var result = new Response.DecisionResult(changeRequestId, change.Status, plan.RoutePlanId,
            isReturn ? proposed[^1].PlannedArrivalAt : Utc(change.NewEstimatedArrivalAt),
            isReturn ? [] : remaining.Select(x => x.RoutePlanRouteId).ToArray(), isReturn ? [] : newIds,
            isReturn ? newIds : [], isReturn ? remaining.Select(x => x.RoutePlanRouteId).ToArray() : [], notified,
            change.IncidentReportId.HasValue && change.EstimatedExtraCost.HasValue, isReturn, Utc(now));
        change.DecisionResponse = Json(result);
        Audit(change, accountId, "ROUTE_CHANGE_" + change.Status, new { status = "PENDING", segments = Read<List<Response.Segment>>(change.CurrentSegments) }, result, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifications.FlushAsync(cancellationToken);
        return result;
    }

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
            await db.Transports.Where(x => x.TransportId == header.TransportId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportId, x => x.TransportId), token);
            await db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), token);
            await db.RoutePlans.Where(x => x.RoutePlanId == routePlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RoutePlanId, x => x.RoutePlanId), token);
            await LockSegmentsAsync(routePlanId, token);
        }
        var plan = await db.RoutePlans.Include(x => x.TransportPlan).ThenInclude(x => x.Transport).ThenInclude(x => x.Booking).ThenInclude(x => x.Customer).ThenInclude(x => x.Account)
            .SingleAsync(x => x.RoutePlanId == routePlanId, token);
        await db.Entry(plan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan).ReloadAsync(token);
        await db.Entry(plan.TransportPlan.Transport).ReloadAsync(token);
        return plan;
    }

    private async Task LockSegmentsAsync(Guid routePlanId, CancellationToken token)
    {
        // Acquire segment locks in the same order for primary and backup routes to avoid deadlocks.
        var ids = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == routePlanId)
            .OrderBy(x => x.RoutePlanRouteId).Select(x => x.RoutePlanRouteId).ToArrayAsync(token);
        foreach (var id in ids)
            await db.RoutePlanRoutes.Where(x => x.RoutePlanRouteId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RoutePlanRouteId, x => x.RoutePlanRouteId), token);
    }

    private static void EnsureInTransit(RoutePlan plan)
    {
        if (plan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("Emergency route change is only available while the transport is IN_TRANSIT.", "TRANSPORT_NOT_IN_TRANSIT");
        if (plan.Status != RoutePlanStatus.ACTIVE || plan.PrimaryRoutePlanId.HasValue)
            throw new ConflictException("Emergency route changes require an ACTIVE primary route plan.", "ROUTE_PLAN_NOT_ACTIVE");
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
        if (x.PlannedArrivalAt is null || x.PlannedDepartureAt is null || x.Route.DistanceKm is null or < 0)
            throw new ConflictException("Route distance and segment schedule are required for BR-036.", "ROUTE_DATA_INCOMPLETE");
        return new(x.RoutePlanRouteId, x.SequenceNo, x.RouteType.ToString(),
            new(x.RouteId, x.Route.RouteCode, x.Route.RouteName, x.Route.TransportMode.ToString(), x.Route.DistanceKm, x.Route.EstimatedDurationMinutes),
            Location(x.Route.FromLocation), Location(x.Route.ToLocation), Utc(x.PlannedDepartureAt.Value), Utc(x.PlannedArrivalAt.Value),
            Utc(x.ActualDepartureAt), Utc(x.ActualArrivalAt), x.ActualArrivalAt.HasValue ? "COMPLETED" : x.ActualDepartureAt.HasValue ? "IN_PROGRESS" : "PLANNED",
            nextDeparture.HasValue ? (decimal)(nextDeparture.Value - x.PlannedArrivalAt.Value).TotalMinutes : null,
            x.TransportProviderId.HasValue ? "OUTSOURCED" : "INTERNAL", x.RoutePlanRouteVehicles.OrderBy(v => v.VehicleId).Select(v => Vehicle(v.Vehicle)).ToList(),
            x.TransportProvider is { } p ? new(new(p.TransportProviderId, p.ProviderCode, p.ProviderName, p.Status.ToString(), Modes(p.SupportedModes)), x.ServiceType, x.ProviderReference) : null,
            Employee(x.DriverEmployee), Employee(x.EscortEmployee), x.Note);
    }

    private static List<Response.Segment> Remaining(List<Response.Segment> active)
    {
        if (active.Count == 0 || active.Any(x => x.ActualDepartureAt.HasValue && !x.ActualArrivalAt.HasValue))
            throw new ConflictException("Stop at a completed segment before replacing the untravelled itinerary.", "ROUTE_PROGRESS_CHANGED");
        var remaining = active.SkipWhile(x => x.ActualArrivalAt.HasValue).ToList();
        if (remaining.Count == 0 || remaining.Any(x => x.ActualArrivalAt.HasValue))
            throw new ConflictException("No continuous untravelled route remains.", "ROUTE_PROGRESS_CHANGED");
        return remaining;
    }

    private async Task<List<Response.Segment>> BackupAsync(Guid backupId, RoutePlan plan, CancellationToken token)
    {
        if (db.Database.IsRelational())
        {
            await db.RoutePlans.Where(x => x.RoutePlanId == backupId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RoutePlanId, x => x.RoutePlanId), token);
            await LockSegmentsAsync(backupId, token);
        }
        var backup = await db.RoutePlans.AsNoTracking().SingleOrDefaultAsync(x => x.RoutePlanId == backupId, token);
        if (backup is null || backup.Status != RoutePlanStatus.CONFIRMED || backup.PrimaryRoutePlanId != plan.RoutePlanId || backup.TransportPlanId != plan.TransportPlanId)
            throw new ConflictException("Backup must be CONFIRMED and linked to this primary route plan.", "BACKUP_NOT_APPROVED");
        return await SegmentsAsync(backupId, token);
    }

    private static List<Response.Segment> BackupRemainder(List<Response.Segment> backup, List<Response.Segment> remaining, DateTimeOffset eta)
    {
        var start = backup.FindIndex(x => x.FromLocation.LocationId == remaining[0].FromLocation.LocationId);
        if (start < 0) throw new BadRequestException("Backup must pass through the current safe stop.");
        var result = backup.Skip(start).ToList();
        var offset = eta - result[^1].PlannedArrivalAt;
        return result.Select((x, i) =>
        {
            var old = remaining.ElementAtOrDefault(i) ?? remaining[^1];
            var internalRoad = x.ExecutionType == "INTERNAL" && x.Route.TransportMode == "ROAD";
            return x with { SequenceNo = i + 1, PlannedDepartureAt = x.PlannedDepartureAt + offset,
                PlannedArrivalAt = x.PlannedArrivalAt + offset, Driver = internalRoad ? x.Driver ?? old.Driver : x.Driver,
                Escort = internalRoad ? x.Escort ?? old.Escort : x.Escort,
                Vehicles = internalRoad && x.Vehicles.Count == 0 ? old.Vehicles : x.Vehicles };
        }).ToList();
    }

    private async Task<List<Response.Segment>> ResolveAsync(List<Request.SegmentRequest> input, List<Response.Segment> remaining, CancellationToken token)
    {
        var result = new List<Response.Segment>();
        var ordered = input.OrderBy(x => x.SequenceNo).ToList();
        foreach (var (item, index) in ordered.Select((x, i) => (x, i)))
        {
            if (item.SequenceNo != index + 1) throw new BadRequestException("sequenceNo must be contiguous from 1.");
            var mode = item.TransportMode is null ? default : Enum.Parse<TransportMode>(item.TransportMode);
            // ponytail: resolve measured masters only; add route creation when a map service supplies road distance and duration.
            var route = await db.Routes.AsNoTracking().Include(x => x.FromLocation).Include(x => x.ToLocation)
                .Where(x => !x.IsDeleted && !x.FromLocation.IsDeleted && !x.ToLocation.IsDeleted)
                .OrderBy(x => x.RouteId).FirstOrDefaultAsync(x => item.RouteId.HasValue ? x.RouteId == item.RouteId :
                    x.FromLocationId == item.FromLocationId && x.ToLocationId == item.ToLocationId && x.TransportMode == mode, token)
                ?? throw new BadRequestException("A measured route master is required; no map service is configured.", "ROUTE_MASTER_REQUIRED");
            if (route.DistanceKm is null or < 0) throw new ConflictException("Route distance is missing.", "ROUTE_DATA_INCOMPLETE");
            var old = remaining.ElementAtOrDefault(index) ?? remaining.Last();
            var internalRoad = item.Outsourcing is null && route.TransportMode == TransportMode.ROAD;
            var vehicles = new List<Response.VehicleBrief>();
            if (item.VehicleIds is { } ids)
            {
                var rows = await db.Vehicles.AsNoTracking().Where(x => ids.Contains(x.VehicleId)).OrderBy(x => x.VehicleId).ToListAsync(token);
                if (rows.Count != ids.Count) throw new BadRequestException("A selected vehicle does not exist.");
                vehicles = rows.Select(Vehicle).ToList();
            }
            else if (internalRoad && old.ExecutionType == "INTERNAL") vehicles = old.Vehicles;
            TransportProvider? provider = null;
            if (item.Outsourcing is { } outsourcing)
                provider = await db.TransportProviders.AsNoTracking().SingleOrDefaultAsync(x => x.TransportProviderId == outsourcing.TransportProviderId, token)
                    ?? throw new BadRequestException("Provider does not exist.", "PROVIDER_NOT_FOUND");
            result.Add(new(Guid.Empty, item.SequenceNo, "PRIMARY", new(route.RouteId, route.RouteCode, route.RouteName, route.TransportMode.ToString(), route.DistanceKm, route.EstimatedDurationMinutes),
                Location(route.FromLocation), Location(route.ToLocation), Utc(DbTime(item.PlannedDepartureAt)), Utc(DbTime(item.PlannedArrivalAt)), null, null, "PLANNED",
                index + 1 < ordered.Count ? (decimal)(DbTime(ordered[index + 1].PlannedDepartureAt) - DbTime(item.PlannedArrivalAt)).TotalMinutes : null,
                provider is null ? "INTERNAL" : "OUTSOURCED", vehicles,
                provider is null ? null : new(new(provider.TransportProviderId, provider.ProviderCode, provider.ProviderName, provider.Status.ToString(), Modes(provider.SupportedModes)), item.Outsourcing!.ServiceType, item.Outsourcing.ProviderReference),
                internalRoad ? old.Driver : null, internalRoad ? old.Escort : null, item.Note ?? item.Outsourcing?.Note));
        }
        return result;
    }

    private static void CheckContinuity(List<Response.Segment> active, List<Response.Segment> proposed, DateTimeOffset eta, bool isReturn)
    {
        var remaining = Remaining(active);
        var lastCompleted = active.LastOrDefault(x => x.ActualArrivalAt.HasValue);
        if (proposed.Count == 0 || proposed[0].FromLocation.LocationId != remaining[0].FromLocation.LocationId ||
            proposed[^1].ToLocation.LocationId != (isReturn ? active[0].FromLocation.LocationId : active[^1].ToLocation.LocationId) ||
            proposed[^1].PlannedArrivalAt != eta || proposed.Any(x => x.ActualDepartureAt.HasValue || x.ActualArrivalAt.HasValue || x.PlannedArrivalAt <= x.PlannedDepartureAt) ||
            lastCompleted?.ActualArrivalAt > proposed[0].PlannedDepartureAt)
            throw new BadRequestException("The proposed itinerary must connect the current position to the destination and match the new ETA.");
        if (proposed.Sum(x => x.Route.DistanceKm ?? 0) + active.Where(x => x.ActualArrivalAt.HasValue).Sum(x => x.Route.DistanceKm ?? 0) > 99999999.99m ||
            (eta - active[0].PlannedDepartureAt).TotalMinutes > int.MaxValue)
            throw new BadRequestException("The proposed distance or duration exceeds the plan storage limit.");
        for (var i = 1; i < proposed.Count; i++)
            if (proposed[i - 1].ToLocation.LocationId != proposed[i].FromLocation.LocationId || proposed[i].PlannedDepartureAt < proposed[i - 1].PlannedArrivalAt)
                throw new BadRequestException("Segments must connect without overlapping schedules.");
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

    private async Task<List<Response.Segment>> ReturnAsync(Request.DecisionRequest request, List<Response.Segment> active,
        List<Response.Segment> remaining, RoutePlan plan, CancellationToken token)
    {
        if (request.ReturnSegments is not null) return await ResolveAsync(request.ReturnSegments, remaining, token);
        var backupIds = await db.RoutePlans.AsNoTracking().Where(x => x.PrimaryRoutePlanId == plan.RoutePlanId &&
            x.TransportPlanId == plan.TransportPlanId && x.Status == RoutePlanStatus.CONFIRMED).OrderBy(x => x.RoutePlanId).Select(x => x.RoutePlanId).ToListAsync(token);
        foreach (var id in backupIds)
        {
            var segments = await BackupAsync(id, plan, token);
            if (segments.Count > 0 && segments.All(x => x.RouteType == "RETURN") &&
                segments[0].FromLocation.LocationId == remaining[0].FromLocation.LocationId && segments[^1].ToLocation.LocationId == active[0].FromLocation.LocationId)
            {
                var shift = Utc(Now) - segments[0].PlannedDepartureAt;
                return BackupRemainder(segments, remaining, segments[^1].PlannedArrivalAt + shift);
            }
        }
        throw new ConflictException("No confirmed return route exists. Provide manager-approved returnSegments.", "SAFE_RETURN_ROUTE_REQUIRED");
    }

    private static bool JsonEquivalent(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        static bool Equal(JsonElement x, JsonElement y) => x.ValueKind == y.ValueKind && (x.ValueKind switch
        {
            JsonValueKind.Object => x.EnumerateObject().Count() == y.EnumerateObject().Count() &&
                x.EnumerateObject().All(p => y.TryGetProperty(p.Name, out var value) && Equal(p.Value, value)),
            JsonValueKind.Array => x.GetArrayLength() == y.GetArrayLength() && x.EnumerateArray().Zip(y.EnumerateArray()).All(p => Equal(p.First, p.Second)),
            JsonValueKind.Number => x.GetDecimal() == y.GetDecimal(),
            _ => x.ToString() == y.ToString()
        });
        return Equal(a.RootElement, b.RootElement);
    }

    private void Audit(RouteChangeRequest change, Guid accountId, string action, object before, object after, DateTime now) =>
        db.CatalogAuditLogs.Add(new CatalogAuditLog
        {
            CatalogAuditLogId = Guid.NewGuid(), EntityType = "RouteChangeRequest", EntityId = change.RouteChangeRequestId,
            ActionType = action, ActorAccountId = accountId, Summary = change.DecisionNote ?? change.Reason,
            BeforeData = JsonDocument.Parse(Json(before)), AfterData = JsonDocument.Parse(Json(after)), CreatedAt = now
        });

    private void Notify(Account account, Guid transportId, RouteChangeRequest change, string title, string message, DateTime now)
    {
        if (account.IsDeleted || account.Status != AccountStatus.ACTIVE)
            throw new ConflictException("A notification recipient is unavailable.", "RECIPIENT_UNAVAILABLE");
        notifications.Queue(new NotificationCreateRequest
        {
            AccountId = account.AccountId, TransportId = transportId, Type = NotificationType.ROUTE, Priority = NotificationPriority.URGENT,
            Title = title, Message = message, ReferenceType = "RouteChangeRequest", ReferenceId = change.RouteChangeRequestId,
            ActionUrl = $"/route-change-requests/{change.RouteChangeRequestId}", CreatedAt = now
        });
        OutboxQueue.AddEmail(db, new MailService.MailContent { To = account.Email, Subject = title, Body = WebUtility.HtmlEncode(message) }, now);
    }
}
