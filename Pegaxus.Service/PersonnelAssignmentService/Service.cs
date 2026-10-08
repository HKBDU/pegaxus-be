using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;
using Pegaxus.Service.OutboxService;

namespace Pegaxus.Service.PersonnelAssignmentService;

public sealed class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.AssignRequest> assignValidator, IValidator<Request.ReplaceRequest> replaceValidator,
    NotificationService.IService notifications, ILogger<Service> logger) : IService
{
    public async Task<Response.AssignmentsResponse> GetAsync(Guid transportPlanId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, false, cancellationToken);
        if (!await db.TransportPlans.AnyAsync(x => x.TransportPlanId == transportPlanId, cancellationToken))
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");
        var phase1 = await db.EmployeeTransportPlans.AsNoTracking().Include(x => x.Employee)
            .Where(x => x.TransportPlanId == transportPlanId).ToListAsync(cancellationToken);
        var candidates = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == transportPlanId && x.Status == RoutePlanStatus.CONFIRMED &&
            x.TransportPlan.Status != TransportPlanStatus.CANCELLED && x.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
            x.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT && x.TransportPlan.Transport.Status != TransportStatus.CANCELLED && x.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
            x.RoutePlanRoutes.Any() && x.RoutePlanRoutes.All(s => s.RouteType == RouteType.PRIMARY)).ToListAsync(cancellationToken);
        if (candidates.Count > 1) throw new ConflictException("Multiple confirmed PRIMARY routes exist for this plan.", "AMBIGUOUS_PRIMARY_ROUTE");
        var route = candidates.SingleOrDefault();
        var segments = route is null ? [] : await LoadSegmentsAsync(route.RoutePlanId, false, cancellationToken);
        return new(transportPlanId, new(
            Brief(phase1.FirstOrDefault(x => x.Responsibility == "TRANSPORT_SPECIALIST" && x.IsPrimary)?.Employee),
            Brief(phase1.FirstOrDefault(x => x.Responsibility == "ROUTE_COORDINATOR" && x.IsPrimary)?.Employee)),
            new(route is not null, route?.RoutePlanId, PersonnelStatus(segments), segments.Select(Map).ToArray()),
            route is null ? null : Offset(route.UpdatedAt));
    }

    public async Task<Response.AssignResponse> AssignAsync(Guid routePlanId, Request.AssignRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, true, cancellationToken);
        Validate(request, assignValidator);
        return await WriteAsync(routePlanId, request.Segments, request.LastUpdatedAt, null, accountId, cancellationToken);
    }

    public async Task<Response.ReplaceResponse> ReplaceAsync(Guid routePlanId, Guid routePlanRouteId, Request.ReplaceRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, true, cancellationToken);
        Validate(request, replaceValidator);
        var result = await WriteAsync(routePlanId, [new() { RoutePlanRouteId = routePlanRouteId,
            DriverEmployeeId = request.DriverEmployeeId, EscortEmployeeId = request.EscortEmployeeId }],
            request.LastUpdatedAt, request.Reason.Trim(), accountId, cancellationToken);
        return new(routePlanId, result.Segments.Single(x => x.RoutePlanRouteId == routePlanRouteId), result.PersonnelStatus, result.UpdatedAt);
    }

    private async Task<Response.AssignResponse> WriteAsync(Guid routePlanId, IReadOnlyList<Request.SegmentRequest> changes,
        DateTimeOffset version, string? reason, Guid actor, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var planId = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
            .Select(x => (Guid?)x.TransportPlanId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Route plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsNpgsql())
        {
            var transportId = await db.TransportPlans.AsNoTracking().Where(x => x.TransportPlanId == planId).Select(x => x.TransportId).SingleAsync(cancellationToken);
            await db.Transports.FromSqlInterpolated($"SELECT * FROM \"Transport\" WHERE transport_id = {transportId} FOR UPDATE").AsNoTracking().LoadAsync(cancellationToken);
            await db.TransportPlans.FromSqlInterpolated($"SELECT * FROM \"TransportPlan\" WHERE transport_plan_id = {planId} FOR UPDATE").LoadAsync(cancellationToken);
        }
        var route = db.Database.IsNpgsql()
            ? await db.RoutePlans.FromSqlInterpolated($"SELECT * FROM \"RoutePlan\" WHERE route_plan_id = {routePlanId} FOR UPDATE").SingleAsync(cancellationToken)
            : await db.RoutePlans.SingleAsync(x => x.RoutePlanId == routePlanId, cancellationToken);
        if (db.Database.IsRelational()) await db.Entry(route).ReloadAsync(cancellationToken);
        if (route.UpdatedAt.Ticks != version.UtcDateTime.Ticks)
            throw new ConflictException("The route plan changed. Reload it and try again.", "ROUTE_PLAN_MODIFIED");
        var plan = await db.TransportPlans.AsNoTracking().Include(x => x.Transport).SingleAsync(x => x.TransportPlanId == planId, cancellationToken);
        if (plan.Status is TransportPlanStatus.COMPLETED or TransportPlanStatus.CANCELLED ||
            plan.Transport.Status is TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED or TransportStatus.CANCELLED)
            throw new ConflictException("Cannot change personnel after departure or completion/cancellation.", "PLAN_LOCKED");
        var originals = await LoadSegmentsAsync(routePlanId, true, cancellationToken);
        var segments = originals.Select(x =>
        {
            var copy = (RoutePlanRoute)db.Entry(x).CurrentValues.ToObject();
            copy.DriverEmployee = x.DriverEmployee; copy.EscortEmployee = x.EscortEmployee;
            copy.TransportProvider = x.TransportProvider; copy.Route = x.Route;
            return copy;
        }).ToList();
        if (route.Status != RoutePlanStatus.CONFIRMED || segments.Count == 0 || segments.Any(x => x.RouteType != RouteType.PRIMARY))
            throw new UnprocessableEntityException("Cannot assign Driver and Escort until the PRIMARY Route Plan is Confirmed.", "ROUTE_NOT_APPROVED");
        var before = Snapshot(segments);
        var updated = new List<RoutePlanRoute>();
        foreach (var change in changes)
        {
            var segment = segments.SingleOrDefault(x => x.RoutePlanRouteId == change.RoutePlanRouteId)
                ?? throw new NotFoundException("Segment not found in this route plan", "SEGMENT_NOT_FOUND");
            if (segment.TransportProviderId.HasValue)
                throw new UnprocessableEntityException("An outsourced segment cannot have a driver or escort", "OUTSOURCED_SEGMENT");
            if (segment.Route.TransportMode != TransportMode.ROAD)
                throw new UnprocessableEntityException("AIR/SEA segments require an outsourcing provider.", "OUTSOURCING_REQUIRED");
            if (segment.ActualDepartureAt.HasValue || segment.ActualArrivalAt.HasValue)
                throw new ConflictException("Cannot change personnel on a started or completed segment", "SEGMENT_LOCKED");
            if (!segment.PlannedDepartureAt.HasValue || !segment.PlannedArrivalAt.HasValue || segment.PlannedDepartureAt >= segment.PlannedArrivalAt)
                throw new BadRequestException("A valid departure and arrival schedule is required before assigning personnel.", "INVALID_SEGMENT_SCHEDULE");
            var driver = change.DriverEmployeeId ?? (reason is not null ? segment.DriverEmployeeId : null);
            var escort = change.EscortEmployeeId ?? (reason is not null ? segment.EscortEmployeeId : null);
            if (!driver.HasValue || !escort.HasValue || driver == escort)
                throw new BadRequestException("Each internal segment requires a different Driver and Escort.");
            if (segment.DriverEmployeeId != driver || segment.EscortEmployeeId != escort) updated.Add(segment);
            segment.DriverEmployeeId = driver;
            segment.EscortEmployeeId = escort;
        }
        var employeeIds = segments.Where(x => !x.TransportProviderId.HasValue)
            .SelectMany(x => new[] { x.DriverEmployeeId, x.EscortEmployeeId }).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().OrderBy(x => x).ToArray();
        var employees = new Dictionary<Guid, Employee>();
        // Employee row locks serialize schedule checks even when two different plans select the same person.
        foreach (var id in employeeIds)
        {
            var employee = db.Database.IsNpgsql()
                ? await db.Employees.FromSqlInterpolated($"SELECT * FROM \"Employee\" WHERE employee_id = {id} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
                : await db.Employees.SingleOrDefaultAsync(x => x.EmployeeId == id, cancellationToken);
            if (employee is null) throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
            if (db.Database.IsRelational()) await db.Entry(employee).ReloadAsync(cancellationToken);
            var account = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == employee.AccountId, cancellationToken);
            if (employee.IsDeleted || !employee.IsActive || account.IsDeleted || account.Status != AccountStatus.ACTIVE || account.AccountType != AccountType.EMPLOYEE)
                throw new NotFoundException("Employee not found or inactive", "EMPLOYEE_NOT_FOUND");
            employees.Add(id, employee);
        }
        foreach (var segment in segments.Where(x => !x.TransportProviderId.HasValue))
        {
            if (segment.DriverEmployeeId is Guid driver && employees[driver].Role != EmployeeRole.DRIVER ||
                segment.EscortEmployeeId is Guid escort && employees[escort].Role != EmployeeRole.ESCORT)
                throw new UnprocessableEntityException("The selected employee does not have the required role", "INVALID_ROLE_ASSIGNMENT");
        }
        foreach (var segment in changes.Select(x => segments.Single(s => s.RoutePlanRouteId == x.RoutePlanRouteId)))
        {
            var nowForConflict = Now();
            foreach (var employeeId in new[] { segment.DriverEmployeeId!.Value, segment.EscortEmployeeId!.Value })
            {
                var conflict = segments.FirstOrDefault(x => x.RoutePlanRouteId != segment.RoutePlanRouteId && !x.TransportProviderId.HasValue &&
                    (x.DriverEmployeeId == employeeId || x.EscortEmployeeId == employeeId) && x.ActualArrivalAt == null && Overlaps(segment, x));
                var external = await db.RoutePlanRoutes.AsNoTracking().Where(x => x.RoutePlanId != routePlanId &&
                    (x.DriverEmployeeId == employeeId || x.EscortEmployeeId == employeeId) && x.ActualArrivalAt == null &&
                    x.RoutePlan.Status != RoutePlanStatus.CANCELLED && x.RoutePlan.Status != RoutePlanStatus.COMPLETED &&
                    x.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED && x.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
                    x.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED && x.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
                    (!x.PlannedDepartureAt.HasValue || !x.PlannedArrivalAt.HasValue || x.PlannedArrivalAt <= x.PlannedDepartureAt ||
                     x.ActualDepartureAt.HasValue && x.PlannedArrivalAt < nowForConflict ||
                     x.PlannedDepartureAt <= segment.PlannedArrivalAt && x.PlannedArrivalAt >= segment.PlannedDepartureAt))
                    .Select(x => x.RoutePlan.TransportPlan.Transport.TransportCode).FirstOrDefaultAsync(cancellationToken);
                if (conflict is not null || external is not null)
                    throw new ConflictException($"{employees[employeeId].Role} {employees[employeeId].EmployeeCode} is already assigned to Transport {external ?? plan.Transport.TransportCode} during this time slot.", "SCHEDULE_CONFLICT");
            }
        }
        foreach (var segment in segments.Where(x => x.TransportProviderId.HasValue))
        {
            if (segment.DriverEmployeeId.HasValue || segment.EscortEmployeeId.HasValue)
                throw new UnprocessableEntityException("An outsourced segment cannot have a driver or escort", "OUTSOURCED_SEGMENT");
            var provider = segment.TransportProvider;
            if (provider is null || provider.IsDeleted || provider.Status != ProviderStatus.ACTIVE)
                throw new UnprocessableEntityException("Provider is not ACTIVE", "PROVIDER_INACTIVE");
        }
        var firstAssignment = !await db.CatalogAuditLogs.AnyAsync(x => x.EntityId == routePlanId && x.EntityType == nameof(RoutePlan) &&
            x.ActionType == "PERSONNEL_ASSIGNED", cancellationToken);
        if (updated.Count == 0 && (reason is not null || !firstAssignment))
            return new(routePlanId, PersonnelStatus(segments), segments.Select(Map).ToArray(), Offset(route.UpdatedAt));
        var now = new DateTime(Math.Max(Now().Ticks, route.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        route.UpdatedAt = now;
        foreach (var segment in updated)
        {
            var original = originals.Single(x => x.RoutePlanRouteId == segment.RoutePlanRouteId);
            original.DriverEmployeeId = segment.DriverEmployeeId;
            original.EscortEmployeeId = segment.EscortEmployeeId;
            original.UpdatedAt = now;
        }
        foreach (var segment in segments)
        {
            segment.DriverEmployee = segment.DriverEmployeeId is Guid driver ? employees.GetValueOrDefault(driver) : null;
            segment.EscortEmployee = segment.EscortEmployeeId is Guid escort ? employees.GetValueOrDefault(escort) : null;
        }
        await SyncPlanLinksAsync(planId, now, cancellationToken);
        var after = Snapshot(segments);
        db.CatalogAuditLogs.Add(new() { CatalogAuditLogId = Guid.NewGuid(), EntityType = nameof(RoutePlan), EntityId = routePlanId,
            ActionType = reason is null ? "PERSONNEL_ASSIGNED" : "PERSONNEL_REPLACED", ActorAccountId = actor, Summary = reason,
            BeforeData = before, AfterData = after, CreatedAt = now });
        var oldIds = before.RootElement.EnumerateArray().SelectMany(x => new[] { x.GetProperty("driver"), x.GetProperty("escort") })
            .Where(x => x.ValueKind != JsonValueKind.Null).Select(x => x.GetProperty("employeeId").GetGuid());
        var recipients = oldIds.Concat(employeeIds).Distinct().ToArray();
        var recipientEmployees = await db.Employees.AsNoTracking().Include(x => x.Account).Where(x => recipients.Contains(x.EmployeeId)).ToListAsync(cancellationToken);
        var schedule = string.Join("\n", segments.Select(x => $"Segment {x.SequenceNo}: {x.PlannedDepartureAt:O} - {x.PlannedArrivalAt:O}; " +
            $"Driver {x.DriverEmployee?.EmployeeCode ?? "-"}; Escort {x.EscortEmployee?.EmployeeCode ?? "-"}; Provider {x.TransportProvider?.ProviderName ?? "-"}"));
        var message = $"{route.RoutePlanCode}: {(reason is null ? "Personnel assigned" : "Personnel replaced: " + reason)}\n{schedule}";
        foreach (var employee in recipientEmployees)
        {
            notifications.Queue(new() { AccountId = employee.AccountId, TransportId = plan.TransportId, Type = NotificationType.ROUTE,
                Priority = NotificationPriority.HIGH, Title = reason is null ? "Personnel assigned" : "Personnel replaced", Message = message,
                ReferenceType = nameof(RoutePlan), ReferenceId = routePlanId, CreatedAt = now });
            QueueEmail(employee.Account.Email, route.RoutePlanCode, message, now);
        }
        if (reason is null)
            foreach (var provider in segments.Where(x => x.TransportProvider is not null).Select(x => x.TransportProvider!).DistinctBy(x => x.TransportProviderId))
                QueueEmail(provider.Email, route.RoutePlanCode, $"Outsourcing confirmed for {route.RoutePlanCode}:\n" +
                    string.Join("\n", segments.Where(x => x.TransportProviderId == provider.TransportProviderId)
                        .Select(x => $"Segment {x.SequenceNo}: {x.PlannedDepartureAt:O} - {x.PlannedArrivalAt:O}")), now);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The route plan changed. Reload it and try again.", "ROUTE_PLAN_MODIFIED"); }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Personnel on route {RoutePlanId} assigned by account {AccountId}; changed segments {Count}", routePlanId, actor, updated.Count);
        await notifications.FlushAsync(CancellationToken.None);
        return new(routePlanId, PersonnelStatus(segments), segments.Select(Map).ToArray(), Offset(now));
    }

    private async Task SyncPlanLinksAsync(Guid planId, DateTime now, CancellationToken cancellationToken)
    {
        var references = await db.RoutePlanRoutes.Where(x => x.RoutePlan.TransportPlanId == planId && x.RoutePlan.Status != RoutePlanStatus.CANCELLED).ToListAsync(cancellationToken);
        var desired = references.SelectMany(x => new[] { (Id: x.DriverEmployeeId, Role: "DRIVER"), (Id: x.EscortEmployeeId, Role: "ESCORT") })
            .Where(x => x.Id.HasValue).Distinct().ToArray();
        var links = await db.EmployeeTransportPlans.Where(x => x.TransportPlanId == planId).ToListAsync(cancellationToken);
        db.EmployeeTransportPlans.RemoveRange(links.Where(x => x.Responsibility is "DRIVER" or "ESCORT" && !desired.Any(d => d.Id == x.EmployeeId)));
        foreach (var item in desired)
        {
            var link = links.SingleOrDefault(x => x.EmployeeId == item.Id);
            if (link is null) db.EmployeeTransportPlans.Add(new() { EmployeeId = item.Id!.Value, TransportPlanId = planId,
                Responsibility = item.Role, AssignedAt = now, IsPrimary = false });
            else if (link.Responsibility != item.Role)
                throw new UnprocessableEntityException("Employee already holds another responsibility in this plan.", "INVALID_ROLE_ASSIGNMENT");
        }
    }

    private async Task<List<RoutePlanRoute>> LoadSegmentsAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        if (tracked && db.Database.IsNpgsql())
            await db.RoutePlanRoutes.FromSqlInterpolated($"SELECT * FROM \"RoutePlanRoute\" WHERE route_plan_id = {id} ORDER BY route_plan_route_id FOR UPDATE").LoadAsync(cancellationToken);
        var query = db.RoutePlanRoutes.Include(x => x.DriverEmployee).Include(x => x.EscortEmployee).Include(x => x.TransportProvider).Include(x => x.Route)
            .Where(x => x.RoutePlanId == id).OrderBy(x => x.SequenceNo);
        if (!tracked) return await query.AsNoTracking().ToListAsync(cancellationToken);
        var result = await query.ToListAsync(cancellationToken);
        if (db.Database.IsRelational()) foreach (var segment in result) await db.Entry(segment).ReloadAsync(cancellationToken);
        return result;
    }

    private async Task AuthorizeAsync(Guid id, bool write, CancellationToken cancellationToken)
    {
        if (!await db.Employees.AsNoTracking().AnyAsync(x => x.AccountId == id && !x.IsDeleted && x.IsActive && !x.Account.IsDeleted &&
            x.Account.Status == AccountStatus.ACTIVE && x.Account.AccountType == AccountType.EMPLOYEE &&
            (x.Role == EmployeeRole.LOGISTICS_MANAGER || !write && (x.Role == EmployeeRole.TRANSPORT_SPECIALIST || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR)), cancellationToken))
            throw new ForbiddenException("The account cannot access personnel assignments.");
    }
    private void QueueEmail(string? to, string subject, string message, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(to)) OutboxQueue.AddEmail(db, new MailService.MailContent {
            To = to, Subject = subject, Body = "<pre>" + WebUtility.HtmlEncode(message) + "</pre>" }, now);
    }
    private static bool Overlaps(RoutePlanRoute a, RoutePlanRoute b) => !b.PlannedDepartureAt.HasValue || !b.PlannedArrivalAt.HasValue ||
        b.PlannedArrivalAt <= b.PlannedDepartureAt || a.PlannedDepartureAt <= b.PlannedArrivalAt && a.PlannedArrivalAt >= b.PlannedDepartureAt;
    private static Response.EmployeeBrief? Brief(Employee? employee) => employee is null ? null : new(employee.EmployeeId, employee.EmployeeCode, employee.FullName, employee.Role.ToString());
    private static Response.SegmentResponse Map(RoutePlanRoute x) => new(x.RoutePlanRouteId, x.SequenceNo, x.TransportProviderId.HasValue ? "OUTSOURCED" : "INTERNAL",
        Offset(x.PlannedDepartureAt), Offset(x.PlannedArrivalAt), Brief(x.DriverEmployee), Brief(x.EscortEmployee),
        x.TransportProvider is null ? null : new(x.TransportProvider.TransportProviderId, x.TransportProvider.ProviderCode, x.TransportProvider.ProviderName),
        x.TransportProviderId.HasValue ? !x.DriverEmployeeId.HasValue && !x.EscortEmployeeId.HasValue : x.DriverEmployeeId.HasValue && x.EscortEmployeeId.HasValue);
    private static string PersonnelStatus(IReadOnlyList<RoutePlanRoute> segments) => segments.Count > 0 && segments.All(x => Map(x).IsComplete)
        ? "FULLY_ASSIGNED" : segments.Any(x => x.DriverEmployeeId.HasValue || x.EscortEmployeeId.HasValue || x.TransportProviderId.HasValue) ? "PARTIALLY_ASSIGNED" : "NOT_ASSIGNED";
    private static JsonDocument Snapshot(IReadOnlyList<RoutePlanRoute> segments) => JsonSerializer.SerializeToDocument(segments.Select(Map), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Validate<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (!result.IsValid) throw new BadRequestException(string.Join(" ", result.Errors.Select(x => x.ErrorMessage)));
    }
    private DateTime Now() => new(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
    private static DateTimeOffset Offset(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    private static DateTimeOffset? Offset(DateTime? time) => time.HasValue ? Offset(time.Value) : null;
}
