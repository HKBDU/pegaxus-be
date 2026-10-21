using System.Data;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.TransportPlanService;

public sealed partial class Service
{
    public async Task<Response.Phase1AssignmentResponse> AssignPhase1Async(
        Guid accountId,
        Guid transportPlanId,
        Request.Phase1AssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (request.TransportSpecialistId == Guid.Empty || request.RouteCoordinatorId == Guid.Empty)
            throw new BadRequestException("Both transportSpecialistId and routeCoordinatorId are required", "VALIDATION_ERROR");
        if (request.TransportSpecialistId == request.RouteCoordinatorId)
            throw new BadRequestException("The Transport Specialist and Route Coordinator must be different employees", "VALIDATION_ERROR");

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId), x => x.TransportPlanId, cancellationToken);

        var plan = await db.TransportPlans
            .Include(item => item.Transport)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        if (plan is null)
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");
        if (plan.Status is TransportPlanStatus.IN_PROGRESS or TransportPlanStatus.COMPLETED or TransportPlanStatus.CANCELLED ||
            plan.Transport.Status is TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED or TransportStatus.CANCELLED)
            throw new ConflictException("Cannot change assignments once transport is in progress, completed or cancelled.", "PLAN_LOCKED");
        if (plan.Status != TransportPlanStatus.APPROVED)
            throw new UnprocessableEntityException(
                "Only an approved TransportPlan can receive phase 1 personnel assignments",
                "DEPOSIT_REQUIRED");
        if (plan.EmployeeTransportPlans.Any(item => item.IsPrimary &&
                item.Responsibility is "TRANSPORT_SPECIALIST" or "FLEET_ROUTE_COORDINATOR"))
            throw new ConflictException(
                "Phase 1 personnel has already been assigned for this Transport Plan",
                "PHASE1_ALREADY_ASSIGNED");

        var employees = await db.Employees
            .Include(item => item.Account)
            .Active()
            .Where(item => (item.EmployeeId == request.TransportSpecialistId || item.EmployeeId == request.RouteCoordinatorId))
            .ToListAsync(cancellationToken);
        var specialist = employees.SingleOrDefault(item => item.EmployeeId == request.TransportSpecialistId);
        var coordinator = employees.SingleOrDefault(item => item.EmployeeId == request.RouteCoordinatorId);
        if (specialist is null || coordinator is null)
            {
            var missing = !await db.Employees.AnyAsync(x => x.EmployeeId == request.TransportSpecialistId, cancellationToken) ||
                !await db.Employees.AnyAsync(x => x.EmployeeId == request.RouteCoordinatorId, cancellationToken);
            if (missing) throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
            throw new UnprocessableEntityException("Selected employee is inactive.", "INVALID_ROLE_ASSIGNMENT");
        }
        if (specialist.Role != EmployeeRole.TRANSPORT_SPECIALIST)
            throw new UnprocessableEntityException("transportSpecialistId must reference an active TRANSPORT_SPECIALIST", "INVALID_ROLE_ASSIGNMENT");
        if (coordinator.Role != EmployeeRole.FLEET_ROUTE_COORDINATOR)
            throw new UnprocessableEntityException("routeCoordinatorId must reference an active FLEET_ROUTE_COORDINATOR", "INVALID_ROLE_ASSIGNMENT");

        var now = UtcNowTruncated();
        var assignments = new[]
        {
            new EmployeeTransportPlan
            {
                EmployeeId = specialist.EmployeeId,
                TransportPlanId = plan.TransportPlanId,
                Responsibility = EmployeeRole.TRANSPORT_SPECIALIST.ToString(),
                AssignedAt = now,
                IsPrimary = true
            },
            new EmployeeTransportPlan
            {
                EmployeeId = coordinator.EmployeeId,
                TransportPlanId = plan.TransportPlanId,
                Responsibility = EmployeeRole.FLEET_ROUTE_COORDINATOR.ToString(),
                AssignedAt = now,
                IsPrimary = true
            }
        };
        db.EmployeeTransportPlans.AddRange(assignments);
        plan.UpdatedAt = now;

        foreach (var employee in new[] { specialist, coordinator })
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = employee.AccountId,
                TransportId = plan.TransportId,
                Type = NotificationType.TRANSPORT,
                Priority = NotificationPriority.HIGH,
                Title = "You have been assigned to a Transport Plan",
                Message = $"You have been assigned to Transport Plan {plan.PlanCode} as {RoleLabel.Of(employee.Role)}. Open the plan to review the booking and plan the next steps.",
                ReferenceType = "TransportPlan",
                ReferenceId = plan.TransportPlanId,
                ActionUrl = NotificationLinks.TransportPlan(employee.Role, plan.TransportPlanId),
                CreatedAt = now
            });
        await MailService.MailRecipients.QueueAsync(db, [specialist.AccountId, coordinator.AccountId], now, recipient => MailService.MailRecipients.Mail(recipient,
            $"{MailService.EmailBodies.TransportPlanTitle(MailService.EmailBodies.TransportPlanMailKind.Assigned)} — {plan.PlanCode}",
            MailService.EmailBodies.TransportPlanBody(recipient.FullName, plan.PlanCode, MailService.EmailBodies.TransportPlanMailKind.Assigned,
                recipient.Role is { } role ? RoleLabel.Of(role) : null, NotificationLinks.TransportPlan(recipient.Role, plan.TransportPlanId))), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return new Response.Phase1AssignmentResponse
        {
            TransportPlanId = plan.TransportPlanId,
            AssignedEmployees = assignments
                .Select(item =>
                {
                    var employee = item.EmployeeId == specialist.EmployeeId ? specialist : coordinator;
                    return new Response.Phase1AssignedEmployeeResponse
                    {
                        EmployeeId = employee.EmployeeId,
                        EmployeeCode = employee.EmployeeCode,
                        FullName = employee.FullName,
                        Role = employee.Role.ToString(),
                        Email = employee.Account.Email,
                        AvatarUrl = employee.Account.AvatarUrl,
                        Responsibility = item.Responsibility!,
                        IsPrimary = item.IsPrimary,
                        AssignedAt = AsUtc(item.AssignedAt)
                    };
                })
                .ToList(),
            UpdatedAt = AsUtc(plan.UpdatedAt)
        };
    }

    public async Task<Response.Phase1AssignmentResponse> UpdatePhase1Async(
        Guid accountId,
        Guid transportPlanId,
        Request.UpdatePhase1AssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required", "VALIDATION_ERROR");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BadRequestException("reason is required", "VALIDATION_ERROR");
        if (!request.TransportSpecialistId.HasValue && !request.RouteCoordinatorId.HasValue)
            throw new BadRequestException("At least one employee must be provided", "VALIDATION_ERROR");

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId), x => x.TransportPlanId, cancellationToken);

        var plan = await db.TransportPlans
            .Include(item => item.Transport)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
                    .ThenInclude(item => item.Account)
            .Include(item => item.RoutePlans)
                .ThenInclude(item => item.RoutePlanRoutes)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        if (plan is null)
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");

        if (plan.Status is TransportPlanStatus.IN_PROGRESS or TransportPlanStatus.COMPLETED or TransportPlanStatus.CANCELLED ||
            plan.Transport.Status is TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED or TransportStatus.CANCELLED)
            throw new ConflictException("Cannot change assignments once transport is in progress, completed or cancelled.", "PLAN_LOCKED");

        var currentSpecialist = plan.EmployeeTransportPlans.SingleOrDefault(item =>
            item.IsPrimary && item.Responsibility == EmployeeRole.TRANSPORT_SPECIALIST.ToString());
        var currentCoordinator = plan.EmployeeTransportPlans.SingleOrDefault(item =>
            item.IsPrimary && item.Responsibility == EmployeeRole.FLEET_ROUTE_COORDINATOR.ToString());
        if (currentSpecialist is null || currentCoordinator is null)
            throw new ConflictException(
                "Phase 1 personnel has not been assigned for this Transport Plan",
                "PHASE1_NOT_ASSIGNED");

        var phase2Enabled = plan.RoutePlans.Any(item =>
            item.Status is (RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE or RoutePlanStatus.COMPLETED) &&
            item.RoutePlanRoutes.Count > 0 &&
            item.RoutePlanRoutes.All(route => route.RouteType == RouteType.PRIMARY));
        if (phase2Enabled)
            throw new ConflictException(
                "Phase 1 personnel assignments are locked after phase 2 is enabled",
                "PHASE1_LOCKED");

        EnsureConcurrency(plan.UpdatedAt, request.LastUpdatedAt);

        var requestedIds = new[] { request.TransportSpecialistId, request.RouteCoordinatorId }
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        var employees = await db.Employees
            .Include(item => item.Account)
            .Active()
            .Where(item => requestedIds.Contains(item.EmployeeId))
            .ToListAsync(cancellationToken);

        Employee? replacementSpecialist = null;
        Employee? replacementCoordinator = null;
        if (request.TransportSpecialistId.HasValue)
        {
            replacementSpecialist = employees.SingleOrDefault(item => item.EmployeeId == request.TransportSpecialistId.Value);
            if (replacementSpecialist is null)
            {
                if (!await db.Employees.AnyAsync(x => x.EmployeeId == request.TransportSpecialistId.Value, cancellationToken))
                    throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
                throw new UnprocessableEntityException("Selected employee is inactive.", "INVALID_ROLE_ASSIGNMENT");
            }
            if (replacementSpecialist.Role != EmployeeRole.TRANSPORT_SPECIALIST)
                throw new UnprocessableEntityException("transportSpecialistId must reference an active TRANSPORT_SPECIALIST", "INVALID_ROLE_ASSIGNMENT");
        }
        if (request.RouteCoordinatorId.HasValue)
        {
            replacementCoordinator = employees.SingleOrDefault(item => item.EmployeeId == request.RouteCoordinatorId.Value);
            if (replacementCoordinator is null)
            {
                if (!await db.Employees.AnyAsync(x => x.EmployeeId == request.RouteCoordinatorId.Value, cancellationToken))
                    throw new NotFoundException("Employee not found", "EMPLOYEE_NOT_FOUND");
                throw new UnprocessableEntityException("Selected employee is inactive.", "INVALID_ROLE_ASSIGNMENT");
            }
            if (replacementCoordinator.Role != EmployeeRole.FLEET_ROUTE_COORDINATOR)
                throw new UnprocessableEntityException("routeCoordinatorId must reference an active FLEET_ROUTE_COORDINATOR", "INVALID_ROLE_ASSIGNMENT");
        }

        var replacedAssignments = new List<(Employee OldEmployee, Employee NewEmployee, string Responsibility)>();
        var now = UtcNowTruncated();
        ReplaceAssignment(currentSpecialist, replacementSpecialist, EmployeeRole.TRANSPORT_SPECIALIST, replacedAssignments);
        ReplaceAssignment(currentCoordinator, replacementCoordinator, EmployeeRole.FLEET_ROUTE_COORDINATOR, replacedAssignments);
        AddReplacement(currentSpecialist, replacementSpecialist, plan, now);
        AddReplacement(currentCoordinator, replacementCoordinator, plan, now);
        plan.UpdatedAt = now;

        foreach (var change in replacedAssignments)
        {
            var recipients = new[] { change.OldEmployee, change.NewEmployee }
                .DistinctBy(item => item.AccountId)
                .Where(item => item.AccountId != accountId);
            foreach (var recipient in recipients)
                notificationService.Queue(new NotificationService.NotificationCreateRequest
                {
                    AccountId = recipient.AccountId,
                    TransportId = plan.TransportId,
                    Type = NotificationType.TRANSPORT,
                    Priority = recipient.AccountId == change.NewEmployee.AccountId ? NotificationPriority.HIGH : NotificationPriority.NORMAL,
                    Title = "Transport Plan personnel assignment updated",
                    Message = $"The {RoleLabel.Of(change.NewEmployee.Role)} for Transport Plan {plan.PlanCode} changed from {change.OldEmployee.FullName} to {change.NewEmployee.FullName}. Reason: {request.Reason.Trim()}",
                    ReferenceType = "TransportPlan",
                    ReferenceId = plan.TransportPlanId,
                    ActionUrl = NotificationLinks.TransportPlan(recipient.Role, plan.TransportPlanId),
                    CreatedAt = now
                });
            if (change.NewEmployee.AccountId != accountId)
                await MailService.MailRecipients.QueueAsync(db, [change.NewEmployee.AccountId], now, recipient => MailService.MailRecipients.Mail(recipient,
                    $"{MailService.EmailBodies.TransportPlanTitle(MailService.EmailBodies.TransportPlanMailKind.PersonnelChanged)} — {plan.PlanCode}",
                    MailService.EmailBodies.TransportPlanBody(recipient.FullName, plan.PlanCode, MailService.EmailBodies.TransportPlanMailKind.PersonnelChanged,
                        $"The {RoleLabel.Of(change.NewEmployee.Role)} changed from {change.OldEmployee.FullName} to {change.NewEmployee.FullName}. Reason: {request.Reason.Trim()}",
                        NotificationLinks.TransportPlan(recipient.Role, plan.TransportPlanId))), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return new Response.Phase1AssignmentResponse
        {
            TransportPlanId = plan.TransportPlanId,
            AssignedEmployees = plan.EmployeeTransportPlans
                .Where(item => item.IsPrimary && item.Responsibility is "TRANSPORT_SPECIALIST" or "FLEET_ROUTE_COORDINATOR")
                .OrderBy(item => item.Responsibility)
                .Select(item => new Response.Phase1AssignedEmployeeResponse
                {
                    EmployeeId = item.EmployeeId,
                    EmployeeCode = item.Employee.EmployeeCode,
                    FullName = item.Employee.FullName,
                    Role = item.Employee.Role.ToString(),
                    Email = item.Employee.Account.Email,
                    AvatarUrl = item.Employee.Account.AvatarUrl,
                    Responsibility = item.Responsibility!,
                    IsPrimary = item.IsPrimary,
                    AssignedAt = AsUtc(item.AssignedAt)
                })
                .ToList(),
            UpdatedAt = AsUtc(plan.UpdatedAt)
        };
    }

    public async Task<Response.AssignmentsResponse> GetAssignmentsAsync(
        Guid accountId,
        Guid transportPlanId,
        CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees
            .Include(item => item.Account)
            .Active()
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        if (employee is null)
            throw new ForbiddenException("Active employee access is required");

        var plan = await db.TransportPlans
            .AsNoTracking()
            .Include(item => item.Transport)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
            .Include(item => item.RoutePlans)
                .ThenInclude(item => item.RoutePlanRoutes)
                    .ThenInclude(item => item.DriverEmployee)
            .Include(item => item.RoutePlans)
                .ThenInclude(item => item.RoutePlanRoutes)
                    .ThenInclude(item => item.EscortEmployee)
            .Include(item => item.RoutePlans)
                .ThenInclude(item => item.RoutePlanRoutes)
                    .ThenInclude(item => item.TransportProvider)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        if (plan is null)
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");

        var isPlanEmployee = plan.EmployeeTransportPlans.Any(item => item.EmployeeId == employee.EmployeeId);
        var isSegmentEmployee = plan.RoutePlans
            .SelectMany(item => item.RoutePlanRoutes)
            .Any(item => item.DriverEmployeeId == employee.EmployeeId || item.EscortEmployeeId == employee.EmployeeId);
        if (employee.Role != EmployeeRole.LOGISTICS_MANAGER && !isPlanEmployee && !isSegmentEmployee)
            throw new ForbiddenException("You are not assigned to this Transport Plan");

        var specialist = plan.EmployeeTransportPlans.SingleOrDefault(item =>
            item.IsPrimary && item.Responsibility == EmployeeRole.TRANSPORT_SPECIALIST.ToString());
        var coordinator = plan.EmployeeTransportPlans.SingleOrDefault(item =>
            item.IsPrimary && item.Responsibility == EmployeeRole.FLEET_ROUTE_COORDINATOR.ToString());
        var primary = plan.RoutePlans
            .Where(item => item.Status is not (RoutePlanStatus.CANCELLED or RoutePlanStatus.REJECTED_BY_MANAGER) && item.RoutePlanRoutes.Count > 0 &&
                item.RoutePlanRoutes.All(route => route.RouteType == RouteType.PRIMARY))
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
        var phase2Enabled = primary?.Status is RoutePlanStatus.CONFIRMED or RoutePlanStatus.ACTIVE or RoutePlanStatus.COMPLETED;
        var segments = phase2Enabled && primary is not null
            ? primary.RoutePlanRoutes.OrderBy(item => item.SequenceNo).ToList()
            : [];

        return new Response.AssignmentsResponse
        {
            TransportPlanId = plan.TransportPlanId,
            Phase1 = new Response.Phase1AssignmentsResponse
            {
                IsAssigned = specialist is not null && coordinator is not null,
                IsLocked = phase2Enabled,
                TransportSpecialist = specialist is null ? null : MapAssignmentEmployee(specialist.Employee),
                RouteCoordinator = coordinator is null ? null : MapAssignmentEmployee(coordinator.Employee)
            },
            Phase2 = new Response.Phase2AssignmentsResponse
            {
                Enabled = phase2Enabled,
                RoutePlanId = phase2Enabled ? primary!.RoutePlanId : null,
                IsPersonnelComplete = segments.Where(item => !item.TransportProviderId.HasValue)
                    .All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue),
                Segments = segments.Select(item => new Response.AssignmentSegmentResponse
                {
                    RoutePlanRouteId = item.RoutePlanRouteId,
                    SequenceNo = item.SequenceNo,
                    IsOutsourced = item.TransportProviderId.HasValue,
                    PlannedDepartureAt = AsUtc(item.PlannedDepartureAt),
                    PlannedArrivalAt = AsUtc(item.PlannedArrivalAt),
                    Driver = item.DriverEmployee is null ? null : MapAssignmentEmployee(item.DriverEmployee),
                    Escort = item.EscortEmployee is null ? null : MapAssignmentEmployee(item.EscortEmployee),
                    Provider = item.TransportProvider is null ? null : new Response.AssignmentProviderResponse
                    {
                        TransportProviderId = item.TransportProvider.TransportProviderId,
                        ProviderCode = item.TransportProvider.ProviderCode,
                        ProviderName = item.TransportProvider.ProviderName
                    },
                    IsComplete = item.TransportProviderId.HasValue ||
                        (item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue)
                }).ToList()
            }
        };
    }

    private static Response.AssignmentEmployeeResponse MapAssignmentEmployee(Employee employee) => new()
    {
        EmployeeId = employee.EmployeeId,
        EmployeeCode = employee.EmployeeCode,
        FullName = employee.FullName,
        Role = employee.Role.ToString()
    };

    private void ReplaceAssignment(
        EmployeeTransportPlan current,
        Employee? replacement,
        EmployeeRole role,
        ICollection<(Employee OldEmployee, Employee NewEmployee, string Responsibility)> changes)
    {
        if (replacement is null || replacement.EmployeeId == current.EmployeeId)
            return;

        changes.Add((current.Employee, replacement, role.ToString()));
        db.EmployeeTransportPlans.Remove(current);
        current.TransportPlan.EmployeeTransportPlans.Remove(current);
    }

    private void AddReplacement(
        EmployeeTransportPlan current,
        Employee? replacement,
        TransportPlan plan,
        DateTime assignedAt)
    {
        if (replacement is null || replacement.EmployeeId == current.EmployeeId)
            return;

        var assignment = new EmployeeTransportPlan
        {
            EmployeeId = replacement.EmployeeId,
            TransportPlanId = plan.TransportPlanId,
            Responsibility = current.Responsibility,
            AssignedAt = assignedAt,
            IsPrimary = true,
            Employee = replacement,
            TransportPlan = plan
        };
        db.EmployeeTransportPlans.Add(assignment);
    }
}
