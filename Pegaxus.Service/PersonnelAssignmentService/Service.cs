using System.Data;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Validation;

namespace Pegaxus.Service.PersonnelAssignmentService;

public sealed class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.AssignRequest> assignValidator, IValidator<Request.ReplaceRequest> replaceValidator,
    NotificationService.INotificationService notifications, ILogger<Service> logger) : IPersonnelAssignmentService
{
    public async Task<Response.AssignmentsResponse> GetAsync(Guid transportPlanId, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, false, cancellationToken);
        var plan = await db.TransportPlans.AsNoTracking().Include(x => x.Transport).ThenInclude(x => x.ComplianceDossier)
            .SingleOrDefaultAsync(x => x.TransportPlanId == transportPlanId, cancellationToken)
            ?? throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");
        var phase1 = await db.EmployeeTransportPlans.AsNoTracking().Include(x => x.Employee).ThenInclude(x => x.Account)
            .Where(x => x.TransportPlanId == transportPlanId).ToListAsync(cancellationToken);
        var reader = await db.Employees.AsNoTracking().SingleAsync(x => x.AccountId == accountId, cancellationToken);
        if (reader.Role != EmployeeRole.LOGISTICS_MANAGER &&
            !phase1.Any(x => x.EmployeeId == reader.EmployeeId) &&
            !await db.RoutePlanRoutes.AnyAsync(x => x.RoutePlan.TransportPlanId == transportPlanId &&
                (x.DriverEmployeeId == reader.EmployeeId || x.EscortEmployeeId == reader.EmployeeId), cancellationToken))
            throw new ForbiddenException("Only personnel assigned to this transport plan may view assignments.");
        var candidates = await db.RoutePlans.AsNoTracking().Where(x => x.TransportPlanId == transportPlanId &&
            (x.Status == RoutePlanStatus.CONFIRMED || x.Status == RoutePlanStatus.ACTIVE) &&
            x.TransportPlan.Status != TransportPlanStatus.CANCELLED && x.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
            x.TransportPlan.Transport.Status != TransportStatus.CANCELLED && x.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
            x.RoutePlanRoutes.Any() && x.RoutePlanRoutes.All(s => s.RouteType == RouteType.PRIMARY)).ToListAsync(cancellationToken);
        if (candidates.Count > 1) throw new ConflictException("Multiple confirmed PRIMARY routes exist for this plan.", "AMBIGUOUS_PRIMARY_ROUTE");
        var route = candidates.SingleOrDefault();
        var segments = route is null ? [] : await LoadSegmentsAsync(route.RoutePlanId, false, cancellationToken);
        var specialist = phase1.FirstOrDefault(x => x.Responsibility == "TRANSPORT_SPECIALIST" && x.IsPrimary);
        var coordinator = phase1.FirstOrDefault(x => x.Responsibility == "FLEET_ROUTE_COORDINATOR" && x.IsPrimary);
        var dossier = plan.Transport.ComplianceDossier;
        return new(transportPlanId, new(
            specialist is not null && coordinator is not null, route is not null,
            Brief(specialist?.Employee, specialist?.AssignedAt),
            Brief(coordinator?.Employee, coordinator?.AssignedAt)),
            new(route is not null, route?.RoutePlanId, PersonnelComplete(segments), segments.Select(Map).ToArray()),
            route is null ? null : AsUtc(route.UpdatedAt),
            dossier is null ? null : new(dossier.ComplianceDossierId, dossier.DossierCode, dossier.Status.ToString()));
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
        return new(routePlanId, result.Segments.Single(x => x.RoutePlanRouteId == routePlanRouteId), result.IsPersonnelComplete, result.UpdatedAt);
    }

    private async Task<Response.AssignResponse> WriteAsync(Guid routePlanId, IReadOnlyList<Request.SegmentRequest> changes,
        DateTimeOffset version, string? reason, Guid actor, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        var planId = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
            .Select(x => (Guid?)x.TransportPlanId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Route plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsNpgsql())
        {
            var transportId = await db.TransportPlans.AsNoTracking().Where(x => x.TransportPlanId == planId).Select(x => x.TransportId).SingleAsync(cancellationToken);
            // Updating the key to itself holds a row lock until the transaction ends.
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == transportId), x => x.TransportId, cancellationToken);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == planId), x => x.TransportPlanId, cancellationToken);
        }
        await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == routePlanId), x => x.RoutePlanId, cancellationToken);
        var route = await db.RoutePlans.SingleAsync(x => x.RoutePlanId == routePlanId, cancellationToken);
        if (db.Database.IsRelational()) await db.Entry(route).ReloadAsync(cancellationToken);
        if (route.UpdatedAt.Ticks != version.UtcDateTime.Ticks)
            throw new ConflictException("The route plan changed. Reload it and try again.", "ROUTE_PLAN_MODIFIED");
        var plan = await db.TransportPlans.AsNoTracking().Include(x => x.Transport).SingleAsync(x => x.TransportPlanId == planId, cancellationToken);
        if (plan.Status is TransportPlanStatus.IN_PROGRESS or TransportPlanStatus.COMPLETED or TransportPlanStatus.CANCELLED ||
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
            throw new UnprocessableEntityException("Cannot assign Driver and Escort until the PRIMARY Route Plan is Confirmed.", "ROUTE_NOT_CONFIRMED");
        var before = Snapshot(segments);
        if (changes.Any(x => !segments.Any(s => s.RoutePlanRouteId == x.RoutePlanRouteId)))
            throw new NotFoundException("Segment not found in this route plan", "SEGMENT_NOT_FOUND");
        if (reason is null && segments.Any(x => !x.TransportProviderId.HasValue &&
                (x.DriverEmployeeId.HasValue || x.EscortEmployeeId.HasValue)))
            throw new ConflictException("Personnel already assigned. Use PUT on the segment to change personnel.", "SEGMENT_ALREADY_ASSIGNED");
        if (reason is null && segments.Where(x => !x.TransportProviderId.HasValue)
                .Any(x => !changes.Any(c => c.RoutePlanRouteId == x.RoutePlanRouteId)))
            throw new BadRequestException("Provide Driver and Escort for every internal segment.", "VALIDATION_ERROR");
        var updated = new List<RoutePlanRoute>();
        foreach (var change in changes)
        {
            var segment = segments.SingleOrDefault(x => x.RoutePlanRouteId == change.RoutePlanRouteId)
                ?? throw new NotFoundException("Segment not found in this route plan", "SEGMENT_NOT_FOUND");
            if (segment.TransportProviderId.HasValue)
                throw new UnprocessableEntityException("An outsourced segment cannot have a driver or escort", "OUTSOURCED_SEGMENT");
            if (segment.ActualDepartureAt.HasValue || segment.ActualArrivalAt.HasValue)
                throw new ConflictException("Cannot change personnel on a started or completed segment", "SEGMENT_LOCKED");
            if (reason is not null && (!segment.DriverEmployeeId.HasValue || !segment.EscortEmployeeId.HasValue))
                throw new ConflictException("Assign this segment through POST before replacing personnel.", "SEGMENT_NOT_ASSIGNED");
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
            await db.LockRowsAsync(db.Employees.Where(x => x.EmployeeId == id), x => x.EmployeeId, cancellationToken);
            var employee = await db.Employees.Include(x => x.Account).SingleOrDefaultAsync(x => x.EmployeeId == id, cancellationToken);
            if (employee is null) throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
            if (db.Database.IsRelational()) await db.Entry(employee).ReloadAsync(cancellationToken);
            var account = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == employee.AccountId, cancellationToken);
            if (employee.IsDeleted || !employee.IsActive || account.IsDeleted || account.Status != AccountStatus.ACTIVE || account.AccountType != AccountType.EMPLOYEE)
                throw new UnprocessableEntityException("The selected employee does not have the required role or is not active", "INVALID_ROLE_ASSIGNMENT");
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
                    (x.RoutePlan.Status == RoutePlanStatus.CONFIRMED || x.RoutePlan.Status == RoutePlanStatus.ACTIVE) &&
                    x.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED && x.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
                    x.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED && x.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
                    (!x.PlannedDepartureAt.HasValue || !x.PlannedArrivalAt.HasValue || x.PlannedArrivalAt <= x.PlannedDepartureAt ||
                     x.ActualDepartureAt.HasValue && x.PlannedArrivalAt < nowForConflict ||
                     x.PlannedDepartureAt <= segment.PlannedArrivalAt && x.PlannedArrivalAt >= segment.PlannedDepartureAt))
                    .Select(x => new {
                        x.RoutePlan.TransportPlan.Transport.TransportCode, x.RoutePlan.TransportPlan.PlanCode,
                        FromLocationName = x.Route.FromLocation.LocationName, ToLocationName = x.Route.ToLocation.LocationName,
                        x.PlannedDepartureAt, x.PlannedArrivalAt
                    }).FirstOrDefaultAsync(cancellationToken);
                if (conflict is not null || external is not null)
                    throw new ConflictException($"{employees[employeeId].Role} {employees[employeeId].EmployeeCode} is already assigned to Transport {external?.TransportCode ?? plan.Transport.TransportCode} during this time slot.",
                        "SCHEDULE_CONFLICT", new Response.ScheduleConflictResponse(employeeId, employees[employeeId].EmployeeCode,
                            employees[employeeId].Role.ToString(), segment.RoutePlanRouteId, external?.PlanCode ?? plan.PlanCode,
                            external?.FromLocationName ?? conflict!.Route.FromLocation.LocationName,
                            external?.ToLocationName ?? conflict!.Route.ToLocation.LocationName,
                            AsUtc(external is not null ? external.PlannedDepartureAt : conflict!.PlannedDepartureAt),
                            AsUtc(external is not null ? external.PlannedArrivalAt : conflict!.PlannedArrivalAt)));
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
            return new(routePlanId, PersonnelComplete(segments), segments.Select(Map).ToArray(), AsUtc(route.UpdatedAt));
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
        var oldIds = new HashSet<Guid>();
        var newIds = new HashSet<Guid>();
        foreach (var segment in updated)
        {
            var previous = before.RootElement.EnumerateArray().Single(x => x.GetProperty("routePlanRouteId").GetGuid() == segment.RoutePlanRouteId);
            foreach (var role in new[] { "driver", "escort" })
            {
                var old = previous.GetProperty(role);
                var oldId = old.ValueKind == JsonValueKind.Null ? (Guid?)null : old.GetProperty("employeeId").GetGuid();
                var newId = role == "driver" ? segment.DriverEmployeeId : segment.EscortEmployeeId;
                if (oldId == newId) continue;
                if (oldId.HasValue) oldIds.Add(oldId.Value);
                if (newId.HasValue) newIds.Add(newId.Value);
            }
        }
        var recipients = oldIds.Concat(newIds).Distinct().ToArray();
        if (PersonnelComplete(segments) && plan.Transport.Status is TransportStatus.CREATED or TransportStatus.PLANNING)
        {
            var transport = await db.Transports.SingleAsync(x => x.TransportId == plan.TransportId, cancellationToken);
            transport.Status = TransportStatus.READY;
            transport.UpdatedAt = now;
        }
        var recipientEmployees = await db.Employees.AsNoTracking().Include(x => x.Account).Where(x => recipients.Contains(x.EmployeeId)).ToListAsync(cancellationToken);
        var schedule = string.Join("\n", segments.Select(x => $"Segment {x.SequenceNo}: {x.PlannedDepartureAt:O} - {x.PlannedArrivalAt:O}; " +
            $"Driver {x.DriverEmployee?.EmployeeCode ?? "-"}; Escort {x.EscortEmployee?.EmployeeCode ?? "-"}; Provider {x.TransportProvider?.ProviderName ?? "-"}"));
        var message = $"{route.RoutePlanCode}: {(reason is null ? "Personnel assigned" : "Personnel replaced: " + reason)}\n{schedule}";
        foreach (var employee in recipientEmployees)
        {
            notifications.Queue(new() { AccountId = employee.AccountId, TransportId = plan.TransportId, Type = NotificationType.ROUTE,
                Priority = newIds.Contains(employee.EmployeeId) ? NotificationPriority.HIGH : NotificationPriority.NORMAL, Title = reason is null ? "Personnel assigned" : "Personnel replaced", Message = message,
                ReferenceType = nameof(RoutePlan), ReferenceId = routePlanId, ActionUrl = $"/admin/route-plans/{routePlanId}", CreatedAt = now });
            QueueEmail(employee, route.RoutePlanCode, reason, segments, now);
        }
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The route plan changed. Reload it and try again.", "ROUTE_PLAN_MODIFIED"); }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Personnel on route {RoutePlanId} assigned by account {AccountId}; changed segments {Count}", routePlanId, actor, updated.Count);
        await notifications.FlushAsync(CancellationToken.None);
        return new(routePlanId, PersonnelComplete(segments), segments.Select(Map).ToArray(), AsUtc(now));
    }

    public async Task<Response.AssignableResponse> GetAssignableAsync(Request.AssignableRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(accountId, true, cancellationToken);
        var roles = new[] { "TRANSPORT_SPECIALIST", "FLEET_ROUTE_COORDINATOR", "DRIVER", "ESCORT" };
        var roleName = request.Role?.Trim().ToUpperInvariant();
        if (!roles.Contains(roleName) || request.PageIndex < 1 || request.PageSize is < 1 or > 100 ||
            ((long)request.PageIndex - 1) * request.PageSize > int.MaxValue || request.Search?.Length > 200)
            throw new BadRequestException("Invalid role, search or pagination.", "VALIDATION_ERROR");
        var scheduled = roleName is "DRIVER" or "ESCORT";
        if (scheduled && (!request.AvailableFrom.HasValue || !request.AvailableTo.HasValue ||
                request.AvailableFrom >= request.AvailableTo))
            throw new BadRequestException("Driver/Escort require a valid availableFrom and availableTo.", "INVALID_DATE_RANGE");
        var role = Enum.Parse<EmployeeRole>(roleName!);
        var query = db.Employees.AsNoTracking().Active().Where(x => x.Role == role && x.Account.AccountType == AccountType.EMPLOYEE);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToUpperInvariant();
            query = query.Where(x => x.EmployeeCode.ToUpper().Contains(search) || x.FullName.ToUpper().Contains(search));
        }
        if (scheduled)
        {
            var from = DateTime.SpecifyKind(request.AvailableFrom!.Value.UtcDateTime, DateTimeKind.Unspecified);
            var to = DateTime.SpecifyKind(request.AvailableTo!.Value.UtcDateTime, DateTimeKind.Unspecified);
            var now = Now();
            query = query.Where(employee => !db.RoutePlanRoutes.Any(segment =>
                (segment.DriverEmployeeId == employee.EmployeeId || segment.EscortEmployeeId == employee.EmployeeId) &&
                (segment.RoutePlan.Status == RoutePlanStatus.CONFIRMED || segment.RoutePlan.Status == RoutePlanStatus.ACTIVE) &&
                segment.ActualArrivalAt == null &&
                segment.RoutePlan.TransportPlan.Status != TransportPlanStatus.COMPLETED &&
                segment.RoutePlan.TransportPlan.Status != TransportPlanStatus.CANCELLED &&
                segment.RoutePlan.TransportPlan.Transport.Status != TransportStatus.COMPLETED &&
                segment.RoutePlan.TransportPlan.Transport.Status != TransportStatus.CANCELLED &&
                (!segment.PlannedDepartureAt.HasValue || !segment.PlannedArrivalAt.HasValue ||
                 segment.PlannedArrivalAt <= segment.PlannedDepartureAt ||
                 segment.ActualDepartureAt.HasValue && segment.PlannedArrivalAt < now ||
                 segment.PlannedDepartureAt <= to && segment.PlannedArrivalAt >= from)));
        }
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.EmployeeCode).ThenBy(x => x.EmployeeId)
            .Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new Response.EmployeeBrief(x.EmployeeId, x.EmployeeCode, x.FullName, x.Role.ToString(),
                x.Account.Email, x.Account.AvatarUrl, null))
            .ToArrayAsync(cancellationToken);
        return new(items, request.PageIndex, request.PageSize, count, Paging.TotalPages(count, request.PageSize));
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
        {
            // Acquire each segment lock in a stable order to avoid deadlocks.
            var segmentIds = await db.RoutePlanRoutes.Where(x => x.RoutePlanId == id)
                .OrderBy(x => x.RoutePlanRouteId).Select(x => x.RoutePlanRouteId).ToArrayAsync(cancellationToken);
            foreach (var segmentId in segmentIds)
                await db.LockRowsAsync(db.RoutePlanRoutes.Where(x => x.RoutePlanRouteId == segmentId), x => x.RoutePlanRouteId, cancellationToken);
        }
        var query = db.RoutePlanRoutes.Include(x => x.DriverEmployee).ThenInclude(x => x!.Account)
            .Include(x => x.EscortEmployee).ThenInclude(x => x!.Account).Include(x => x.TransportProvider)
            .Include(x => x.Route).ThenInclude(x => x.FromLocation).Include(x => x.Route).ThenInclude(x => x.ToLocation)
            .Where(x => x.RoutePlanId == id).OrderBy(x => x.SequenceNo);
        if (!tracked) return await query.AsNoTracking().ToListAsync(cancellationToken);
        var result = await query.ToListAsync(cancellationToken);
        if (db.Database.IsRelational()) foreach (var segment in result) await db.Entry(segment).ReloadAsync(cancellationToken);
        return result;
    }

    private async Task AuthorizeAsync(Guid id, bool write, CancellationToken cancellationToken)
    {
        if (!await db.Employees.AsNoTracking().Active().AnyAsync(x => x.AccountId == id && x.Account.AccountType == AccountType.EMPLOYEE && (x.Role == EmployeeRole.LOGISTICS_MANAGER || !write && (x.Role == EmployeeRole.TRANSPORT_SPECIALIST || x.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR || x.Role == EmployeeRole.DRIVER || x.Role == EmployeeRole.ESCORT)), cancellationToken))
            throw new ForbiddenException("The account cannot access personnel assignments.");
    }
    private void QueueEmail(Employee employee, string routePlanCode, string? replaceReason, IEnumerable<RoutePlanRoute> segments, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(employee.Account.Email)) return;
        var isReplaced = replaceReason is not null;
        OutboxQueue.AddEmail(db, new MailService.MailContent
        {
            To = employee.Account.Email,
            Subject = isReplaced ? $"Route assignment updated — {routePlanCode}" : $"New route assignment — {routePlanCode}",
            Body = MailService.EmailBodies.RouteAssignmentBody(
                employeeName: employee.FullName,
                routePlanCode: routePlanCode,
                isReplaced: isReplaced,
                replaceReason: replaceReason,
                actionUrl: null,
                segments: segments.OrderBy(x => x.SequenceNo).Select(x => new MailService.EmailBodies.RouteSegment(
                    x.SequenceNo, x.PlannedDepartureAt, x.PlannedArrivalAt,
                    x.DriverEmployee?.EmployeeCode, x.EscortEmployee?.EmployeeCode, x.TransportProvider?.ProviderName)))
        }, now);
    }
    private static bool Overlaps(RoutePlanRoute a, RoutePlanRoute b) => !b.PlannedDepartureAt.HasValue || !b.PlannedArrivalAt.HasValue ||
        b.PlannedArrivalAt <= b.PlannedDepartureAt || a.PlannedDepartureAt <= b.PlannedArrivalAt && a.PlannedArrivalAt >= b.PlannedDepartureAt;
    private static Response.EmployeeBrief? Brief(Employee? employee, DateTime? assignedAt = null) => employee is null ? null :
        new(employee.EmployeeId, employee.EmployeeCode, employee.FullName, employee.Role.ToString(),
            employee.Account.Email, employee.Account.AvatarUrl, AsUtc(assignedAt));
    private static Response.SegmentResponse Map(RoutePlanRoute x) => new(x.RoutePlanRouteId, x.SequenceNo, x.TransportProviderId.HasValue,
        AsUtc(x.PlannedDepartureAt), AsUtc(x.PlannedArrivalAt), Brief(x.DriverEmployee), Brief(x.EscortEmployee),
        x.TransportProvider is null ? null : new(x.TransportProvider.TransportProviderId, x.TransportProvider.ProviderCode, x.TransportProvider.ProviderName),
        x.TransportProviderId.HasValue ? !x.DriverEmployeeId.HasValue && !x.EscortEmployeeId.HasValue : x.DriverEmployeeId.HasValue && x.EscortEmployeeId.HasValue);
    private static bool PersonnelComplete(IReadOnlyList<RoutePlanRoute> segments) => segments.Count > 0 && segments.All(x => Map(x).IsComplete);
    private static JsonDocument Snapshot(IReadOnlyList<RoutePlanRoute> segments) => JsonSerializer.SerializeToDocument(segments.Select(Map), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private DateTime Now() => new(clock.GetUtcNow().Ticks / 10 * 10, DateTimeKind.Unspecified);
}
