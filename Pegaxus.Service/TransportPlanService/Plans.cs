using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;
using static Pegaxus.Service.Common.Text;

namespace Pegaxus.Service.TransportPlanService;

public sealed partial class Service
{
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

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == transportPlanId), x => x.TransportPlanId, cancellationToken);

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

        var expectedUpdatedAt = Truncate(request.LastUpdatedAt.UtcDateTime);
        if (plan.UpdatedAt != expectedUpdatedAt)
            throw new ConflictException(
                "The Transport Plan has been modified by another request. Please reload and try again.",
                "TRANSPORT_PLAN_CONCURRENCY_CONFLICT");

        await ValidateLocationsAsync(request.OriginLocationId, request.DestinationLocationId, cancellationToken);

        var start = request.PlannedStartAt ?? plan.PlannedStartAt;
        var end = request.PlannedEndAt ?? plan.PlannedEndAt;
        if (start.HasValue && end.HasValue && end <= start)
            throw new BadRequestException("plannedEndAt must be after plannedStartAt");

        var now = UtcNowTruncated();
        plan.OriginLocationId = request.OriginLocationId ?? plan.OriginLocationId;
        plan.DestinationLocationId = request.DestinationLocationId ?? plan.DestinationLocationId;
        plan.PlannedStartAt = request.PlannedStartAt.HasValue
            ? Truncate(request.PlannedStartAt.Value.UtcDateTime)
            : plan.PlannedStartAt;
        plan.PlannedEndAt = request.PlannedEndAt.HasValue
            ? Truncate(request.PlannedEndAt.Value.UtcDateTime)
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
            .Active()
            .Where(item => item.AccountId == accountId)
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
            OriginLocationId = plan.OriginLocationId,
            DestinationLocationId = plan.DestinationLocationId,
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
            ApprovedAt = AsUtc(plan.ApprovedAt),
            CreatedAt = AsUtc(plan.CreatedAt),
            UpdatedAt = AsUtc(plan.UpdatedAt)
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
            .Active()
            .Where(employee => employee.AccountId == accountId)
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
                PlannedStartAt = AsUtc(plan.PlannedStartAt),
                PlannedEndAt = AsUtc(plan.PlannedEndAt),
                UpdatedAt = AsUtc(plan.UpdatedAt)
            })
            .ToListAsync(cancellationToken);

        return new Response.ListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = Paging.TotalPages(totalItems, request.PageSize)
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

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);

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

        await ValidateLocationsAsync(request.OriginLocationId, request.DestinationLocationId, cancellationToken);

        var now = UtcNowTruncated();
        var transportPlan = new TransportPlan
        {
            TransportPlanId = Guid.NewGuid(),
            TransportId = booking.Transport.TransportId,
            OriginLocationId = request.OriginLocationId,
            DestinationLocationId = request.DestinationLocationId,
            PlanCode = await GenerateCodeAsync("TP", code =>
                db.TransportPlans.AnyAsync(plan => plan.PlanCode == code, cancellationToken)),
            Status = TransportPlanStatus.DRAFT,
            PlannedStartAt = Truncate(request.PlannedStartAt.UtcDateTime),
            PlannedEndAt = Truncate(request.PlannedEndAt.UtcDateTime),
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
            OriginLocationId = transportPlan.OriginLocationId,
            DestinationLocationId = transportPlan.DestinationLocationId,
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

    private async Task ValidateLocationsAsync(Guid? originId, Guid? destinationId, CancellationToken cancellationToken)
    {
        foreach (var locationId in new[] { originId, destinationId }.Where(id => id.HasValue).Distinct())
            if (!await db.Locations.AnyAsync(location => location.LocationId == locationId && !location.IsDeleted, cancellationToken))
                throw new NotFoundException("Location not found", "LOCATION_NOT_FOUND");
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
}
