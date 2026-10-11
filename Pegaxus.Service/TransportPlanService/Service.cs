using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.BookingService;
using Pegaxus.Service.Exceptions;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.TransportPlanService;

public sealed class Service : IService
{
    private readonly AppDbContext db;
    private readonly NotificationService.IService notificationService;

    public Service(AppDbContext db, NotificationService.IService? notificationService = null)
    {
        this.db = db;
        this.notificationService = notificationService ??
            new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService());
    }

    public async Task<Response.CreateResponse> UpdateAsync(
        Guid accountId,
        Guid transportPlanId,
        Request.UpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (request.EstimatedDistanceKm < 0)
            throw new BadRequestException("estimatedDistanceKm must not be negative");
        if (request.EstimatedDurationMinutes < 0)
            throw new BadRequestException("estimatedDurationMinutes must not be negative");
        if (request.PlannedStartAt.HasValue && request.PlannedEndAt.HasValue &&
            request.PlannedEndAt <= request.PlannedStartAt)
            throw new BadRequestException("plannedEndAt must be after plannedStartAt");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (db.Database.IsNpgsql())
            await db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), cancellationToken);

        var plan = await db.TransportPlans
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        if (plan is null)
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");
        if (plan.Status is not (TransportPlanStatus.DRAFT or TransportPlanStatus.PENDING or TransportPlanStatus.APPROVED))
            throw new BadRequestException(
                "Cannot edit Transport Plan once the transport is in transit or completed.",
                "INVALID_TRANSPORT_PLAN_STATUS");

        var expectedUpdatedAt = ToDatabasePrecision(request.LastUpdatedAt.UtcDateTime);
        if (plan.UpdatedAt != expectedUpdatedAt)
            throw new ConflictException(
                "The Transport Plan has been modified by another request. Please reload and try again.",
                "TRANSPORT_PLAN_CONCURRENCY_CONFLICT");

        var start = request.PlannedStartAt ?? plan.PlannedStartAt;
        var end = request.PlannedEndAt ?? plan.PlannedEndAt;
        if (start.HasValue && end.HasValue && end <= start)
            throw new BadRequestException("plannedEndAt must be after plannedStartAt");

        var now = UtcNow();
        plan.PlannedStartAt = request.PlannedStartAt.HasValue
            ? ToDatabasePrecision(request.PlannedStartAt.Value.UtcDateTime)
            : plan.PlannedStartAt;
        plan.PlannedEndAt = request.PlannedEndAt.HasValue
            ? ToDatabasePrecision(request.PlannedEndAt.Value.UtcDateTime)
            : plan.PlannedEndAt;
        if (request.EstimatedDistanceKm.HasValue)
            plan.EstimatedDistanceKm = request.EstimatedDistanceKm;
        if (request.EstimatedDurationMinutes.HasValue)
            plan.EstimatedDurationMinutes = request.EstimatedDurationMinutes;
        if (request.PlanningNote is not null)
            plan.PlanningNote = EmptyToNull(request.PlanningNote);
        plan.UpdatedAt = now;

        var reason = EmptyToNull(request.UpdateReason);
        var message = $"Transport Plan {plan.PlanCode} was updated" +
            (reason is null ? "." : $". Reason: {reason}");
        var recipientAccountIds = plan.EmployeeTransportPlans
            .Select(item => item.Employee.AccountId)
            .Append(plan.Transport.Booking.Customer.AccountId)
            .Distinct()
            .ToList();
        foreach (var recipientAccountId in recipientAccountIds)
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipientAccountId,
                TransportId = plan.TransportId,
                Type = NotificationType.TRANSPORT,
                Priority = NotificationPriority.NORMAL,
                Title = "Transport Plan updated",
                Message = message,
                ReferenceType = "TransportPlan",
                ReferenceId = plan.TransportPlanId,
                ActionUrl = $"/transport-plans/{plan.TransportPlanId}",
                CreatedAt = now
            });

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return await GetByIdAsync(accountId, transportPlanId, cancellationToken);
    }

    public async Task<Response.CreateResponse> GetByIdAsync(
        Guid accountId,
        Guid transportPlanId,
        CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees
            .Where(item => item.AccountId == accountId && !item.IsDeleted && item.IsActive &&
                !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
            .Select(item => new { item.EmployeeId, item.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (employee is null || employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or
            EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR))
            throw new ForbiddenException("Only Logistics Managers, Transport Specialists and Fleet Route Coordinators can access transport plans");

        var plan = await db.TransportPlans
            .AsNoTracking()
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.BookingHorses)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
            .Include(item => item.ApprovedByEmployee)
            .SingleOrDefaultAsync(item => item.TransportPlanId == transportPlanId, cancellationToken);
        if (plan is null)
            throw new NotFoundException("Transport plan not found", "TRANSPORT_PLAN_NOT_FOUND");

        if (employee.Role is EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR &&
            !plan.EmployeeTransportPlans.Any(item => item.EmployeeId == employee.EmployeeId))
            throw new ForbiddenException("You are not assigned to this Transport Plan");

        var booking = plan.Transport.Booking;
        return new Response.CreateResponse
        {
            TransportPlanId = plan.TransportPlanId,
            PlanCode = plan.PlanCode,
            Status = plan.Status.ToString(),
            Booking = new Response.BookingResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                IsInternational = booking.OriginCountry != booking.DestinationCountry,
                CustomerName = booking.Customer.FullName,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                TotalHorses = booking.BookingHorses.Count,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                RequestedArrivalDate = booking.RequestedArrivalDate,
                SpecialRequirements = booking.SpecialRequirements
            },
            Transport = new Response.TransportResponse
            {
                TransportId = plan.Transport.TransportId,
                TransportCode = plan.Transport.TransportCode,
                Status = plan.Transport.Status.ToString()
            },
            PlannedStartAt = AsUtc(plan.PlannedStartAt!.Value),
            PlannedEndAt = AsUtc(plan.PlannedEndAt!.Value),
            PlanningNote = plan.PlanningNote,
            EstimatedDistanceKm = plan.EstimatedDistanceKm,
            EstimatedDurationMinutes = plan.EstimatedDurationMinutes,
            AssignedEmployees = plan.EmployeeTransportPlans
                .OrderByDescending(item => item.IsPrimary)
                .ThenByDescending(item => item.AssignedAt)
                .Select(item => new global::Pegaxus.Service.BookingService.Response.BookingAssignmentResponse
                {
                    BookingId = booking.BookingId,
                    TransportPlanId = item.TransportPlanId,
                    EmployeeId = item.EmployeeId,
                    EmployeeCode = item.Employee.EmployeeCode,
                    EmployeeName = item.Employee.FullName,
                    Role = item.Employee.Role.ToString(),
                    AssignedAt = AsUtc(item.AssignedAt),
                    IsPrimary = item.IsPrimary,
                    Responsibility = item.Responsibility,
                    Note = item.Note
                })
                .ToList(),
            ApprovedBy = plan.ApprovedByEmployee is null
                ? null
                : new Response.ApprovedByResponse
                {
                    EmployeeId = plan.ApprovedByEmployee.EmployeeId,
                    FullName = plan.ApprovedByEmployee.FullName
                },
            ApprovedAt = AsUtcNullable(plan.ApprovedAt),
            CreatedAt = AsUtc(plan.CreatedAt),
            UpdatedAt = AsUtc(plan.UpdatedAt)
        };
    }

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

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (db.Database.IsNpgsql())
            await db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), cancellationToken);

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
            .Where(item => (item.EmployeeId == request.TransportSpecialistId || item.EmployeeId == request.RouteCoordinatorId) &&
                !item.IsDeleted && item.IsActive && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
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

        var now = UtcNow();
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
                Message = $"You have been assigned to Transport Plan {plan.PlanCode} as {employee.Role}.",
                ReferenceType = "TransportPlan",
                ReferenceId = plan.TransportPlanId,
                ActionUrl = $"/admin/transport-plans/{plan.TransportPlanId}",
                CreatedAt = now
            });

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

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (db.Database.IsNpgsql())
            await db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TransportPlanId, x => x.TransportPlanId), cancellationToken);

        var plan = await db.TransportPlans
            .Include(item => item.Transport)
            .Include(item => item.EmployeeTransportPlans)
                .ThenInclude(item => item.Employee)
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
            .Where(item => requestedIds.Contains(item.EmployeeId) &&
                !item.IsDeleted && item.IsActive && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
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
        var now = UtcNow();
        ReplaceAssignment(currentSpecialist, replacementSpecialist, EmployeeRole.TRANSPORT_SPECIALIST, replacedAssignments);
        ReplaceAssignment(currentCoordinator, replacementCoordinator, EmployeeRole.FLEET_ROUTE_COORDINATOR, replacedAssignments);
        AddReplacement(currentSpecialist, replacementSpecialist, plan, now);
        AddReplacement(currentCoordinator, replacementCoordinator, plan, now);
        plan.UpdatedAt = now;

        foreach (var change in replacedAssignments)
        {
            var recipientIds = new[] { change.OldEmployee.AccountId, change.NewEmployee.AccountId }
                .Distinct()
                .Where(item => item != accountId);
            foreach (var recipientId in recipientIds)
                notificationService.Queue(new NotificationService.NotificationCreateRequest
                {
                    AccountId = recipientId,
                    TransportId = plan.TransportId,
                    Type = NotificationType.TRANSPORT,
                    Priority = recipientId == change.NewEmployee.AccountId ? NotificationPriority.HIGH : NotificationPriority.NORMAL,
                    Title = "Transport Plan personnel assignment updated",
                    Message = $"The {change.Responsibility} assignment for Transport Plan {plan.PlanCode} was changed. Reason: {request.Reason.Trim()}",
                    ReferenceType = "TransportPlan",
                    ReferenceId = plan.TransportPlanId,
                    ActionUrl = $"/admin/transport-plans/{plan.TransportPlanId}",
                    CreatedAt = now
                });
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
            .SingleOrDefaultAsync(item => item.AccountId == accountId && !item.IsDeleted && item.IsActive &&
                !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE, cancellationToken);
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
            .Where(item => item.Status != RoutePlanStatus.CANCELLED && item.RoutePlanRoutes.Count > 0 &&
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
                    PlannedDepartureAt = AsUtcNullable(item.PlannedDepartureAt),
                    PlannedArrivalAt = AsUtcNullable(item.PlannedArrivalAt),
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

    public async Task<Response.ListResponse> GetListAsync(
        Guid accountId,
        Request.ListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1)
            throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (request.PageSize < 1 || request.PageSize > 100)
            throw new BadRequestException("PageSize must be between 1 and 100");

        var role = await db.Employees
            .Where(employee => employee.AccountId == accountId && !employee.IsDeleted && employee.IsActive &&
                !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not (EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.LOGISTICS_MANAGER))
            throw new ForbiddenException("Only Logistics Managers, Transport Specialists and Fleet Route Coordinators can access transport plans");

        var query = db.TransportPlans
            .AsNoTracking()
            .Where(plan => plan.Transport.Booking.Status == BookingStatus.APPROVED);

        if (role is EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR)
            query = query.Where(plan => plan.EmployeeTransportPlans.Any(assignment => assignment.Employee.AccountId == accountId));

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TransportPlanStatus>(request.Status.Trim(), true, out var status))
                throw new BadRequestException("Transport plan status is invalid", "INVALID_TRANSPORT_PLAN_STATUS");
            query = query.Where(plan => plan.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.RoutePlanStatus))
        {
            if (!Enum.TryParse<RoutePlanStatus>(request.RoutePlanStatus.Trim(), true, out var status))
                throw new BadRequestException("Route plan status is invalid", "INVALID_ROUTE_PLAN_STATUS");
            query = query.Where(plan => plan.RoutePlans.Any(routePlan =>
                routePlan.Status == status &&
                routePlan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.PRIMARY)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(plan =>
                plan.PlanCode.ToLower().Contains(search) ||
                plan.Transport.Booking.BookingCode.ToLower().Contains(search) ||
                plan.Transport.Booking.Customer.FullName.ToLower().Contains(search));
        }

        query = ApplySorting(query, request.SortBy);
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(plan => new Response.ListItemResponse
            {
                TransportPlanId = plan.TransportPlanId,
                PlanCode = plan.PlanCode,
                Status = plan.Status.ToString(),
                BookingId = plan.Transport.BookingId,
                BookingCode = plan.Transport.Booking.BookingCode,
                CustomerName = plan.Transport.Booking.Customer.FullName,
                PlannedStartAt = AsUtcNullable(plan.PlannedStartAt),
                PlannedEndAt = AsUtcNullable(plan.PlannedEndAt),
                UpdatedAt = AsUtc(plan.UpdatedAt)
            })
            .ToListAsync(cancellationToken);

        return new Response.ListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task<Response.CreateResponse> CreateAsync(
        Guid accountId,
        Guid bookingId,
        Request.CreateRequest request,
        CancellationToken cancellationToken = default)
    {
        await GetLogisticsManagerAsync(accountId, cancellationToken);
        if (request.PlannedStartAt == default || request.PlannedEndAt == default)
            throw new BadRequestException("plannedStartAt and plannedEndAt are required");
        if (request.PlannedEndAt <= request.PlannedStartAt)
            throw new BadRequestException("plannedEndAt must be after plannedStartAt");
        if (request.EstimatedDistanceKm < 0)
            throw new BadRequestException("estimatedDistanceKm must not be negative");
        if (request.EstimatedDurationMinutes < 0)
            throw new BadRequestException("estimatedDurationMinutes must not be negative");

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Transport\" WHERE booking_id = {bookingId} FOR UPDATE",
                cancellationToken);

        var booking = await db.Bookings
            .Include(item => item.Customer)
            .Include(item => item.BookingHorses)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException(
                $"This action cannot be performed while the booking is in {booking.Status} status",
                "INVALID_BOOKING_STATUS");
        if (booking.Transport is null)
            throw new ConflictException("The approved booking has no Transport", "TRANSPORT_NOT_FOUND");
        if (booking.Transport.TransportPlans.Count > 0)
            throw new ConflictException(
                "Transport Plan already exists for this booking. Please edit the existing plan.",
                "TRANSPORT_PLAN_EXISTS");

        var now = UtcNow();
        var transportPlan = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(),
            TransportId = booking.Transport.TransportId,
            PlanCode = await GenerateCodeAsync("TP", code =>
                db.TransportPlans.AnyAsync(plan => plan.PlanCode == code, cancellationToken)),
            Status = TransportPlanStatus.DRAFT,
            PlannedStartAt = ToDatabasePrecision(request.PlannedStartAt.UtcDateTime),
            PlannedEndAt = ToDatabasePrecision(request.PlannedEndAt.UtcDateTime),
            EstimatedDistanceKm = request.EstimatedDistanceKm,
            EstimatedDurationMinutes = request.EstimatedDurationMinutes,
            PlanningNote = EmptyToNull(request.PlanningNote),
            CreatedAt = now,
            UpdatedAt = now
        };

        booking.Transport.Status = TransportStatus.PLANNING;
        booking.Transport.UpdatedAt = now;
        db.TransportPlans.Add(transportPlan);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return new Response.CreateResponse
        {
            TransportPlanId = transportPlan.TransportPlanId,
            PlanCode = transportPlan.PlanCode,
            Status = transportPlan.Status.ToString(),
            Booking = new Response.BookingResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                IsInternational = booking.OriginCountry != booking.DestinationCountry,
                CustomerName = booking.Customer.FullName,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                TotalHorses = booking.BookingHorses.Count,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                RequestedArrivalDate = booking.RequestedArrivalDate,
                SpecialRequirements = booking.SpecialRequirements
            },
            Transport = new Response.TransportResponse
            {
                TransportId = booking.Transport.TransportId,
                TransportCode = booking.Transport.TransportCode,
                Status = booking.Transport.Status.ToString()
            },
            PlannedStartAt = AsUtc(transportPlan.PlannedStartAt!.Value),
            PlannedEndAt = AsUtc(transportPlan.PlannedEndAt!.Value),
            PlanningNote = transportPlan.PlanningNote,
            EstimatedDistanceKm = transportPlan.EstimatedDistanceKm,
            EstimatedDurationMinutes = transportPlan.EstimatedDurationMinutes,
            AssignedEmployees = [],
            ApprovedBy = null,
            ApprovedAt = null,
            CreatedAt = AsUtc(transportPlan.CreatedAt),
            UpdatedAt = AsUtc(transportPlan.UpdatedAt)
        };
    }

    private async Task GetLogisticsManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var exists = await db.Employees.AnyAsync(item =>
            item.AccountId == accountId &&
            item.Role == EmployeeRole.LOGISTICS_MANAGER &&
            item.IsActive && !item.IsDeleted &&
            !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        if (!exists)
            throw new ForbiddenException("You do not have permission to perform this action");
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
        plan.EmployeeTransportPlans.Add(assignment);
    }

    private static void EnsureConcurrency(DateTime actualUpdatedAt, DateTimeOffset expectedUpdatedAt)
    {
        if (actualUpdatedAt != ToDatabasePrecision(expectedUpdatedAt.UtcDateTime))
            throw new ConflictException(
                "The Transport Plan has been modified by another request. Please reload and try again.",
                "TRANSPORT_PLAN_CONCURRENCY_CONFLICT");
    }

    private static IQueryable<TransportPlan> ApplySorting(IQueryable<TransportPlan> query, string? sortBy)
    {
        var value = string.IsNullOrWhiteSpace(sortBy) ? "createdAt:desc" : sortBy.Trim();
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            (!parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase) &&
             !parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)) ||
            (parts[0].ToLowerInvariant() != "createdat" && parts[0].ToLowerInvariant() != "plannedstartat"))
            throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER");

        var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        return parts[0].ToLowerInvariant() switch
        {
            "plannedstartat" when ascending => query.OrderBy(plan => plan.PlannedStartAt).ThenBy(plan => plan.TransportPlanId),
            "plannedstartat" => query.OrderByDescending(plan => plan.PlannedStartAt).ThenBy(plan => plan.TransportPlanId),
            "createdat" when ascending => query.OrderBy(plan => plan.CreatedAt).ThenBy(plan => plan.TransportPlanId),
            _ => query.OrderByDescending(plan => plan.CreatedAt).ThenBy(plan => plan.TransportPlanId)
        };
    }

    private static async Task<string> GenerateCodeAsync(string prefix, Func<string, Task<bool>> exists)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = $"{prefix}-{date}-{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await exists(code)) return code;
        }
        throw new InvalidOperationException($"Unable to generate a unique {prefix} code");
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime UtcNow() => ToDatabasePrecision(DateTime.UtcNow);
    private static DateTime ToDatabasePrecision(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTime(unspecified.Ticks - unspecified.Ticks % 10, DateTimeKind.Unspecified);
    }
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? AsUtcNullable(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
