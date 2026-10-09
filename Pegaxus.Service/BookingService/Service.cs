using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.OutboxService;

using BookingEntity = Pegaxus.Repository.Entities.Booking;
using MailService = Pegaxus.Service.MailService;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.BookingService;

public partial class Service : IService
{
    private readonly AppDbContext _db;
    private readonly MailService.IService _mailService;
    private readonly NotificationService.IService _notificationService;

    public Service(
        AppDbContext db,
        MailService.IService mailService,
        NotificationService.IService notificationService)
    {
        _db = db;
        _mailService = mailService;
        _notificationService = notificationService;
    }

    public Service(AppDbContext db, MailService.IService mailService)
        : this(
            db,
            mailService,
            new NotificationService.Service(db, new NotificationService.NoopNotificationRealtimeService()))
    {
    }

    public async Task<Response.BookingDetailResponse> GetAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(item => item.Customer)
            .Include(item => item.BookingHorses)
                .ThenInclude(item => item.Horse)
            .Include(item => item.AssignedEmployee)
            .Include(item => item.Transport)
            .Where(item =>
                item.BookingId == bookingId &&
                (canViewAll && item.Status != BookingStatus.DRAFT ||
                 !canViewAll && item.Customer.AccountId == accountId))
            .SingleOrDefaultAsync(cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        return new Response.BookingDetailResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Status = booking.Status.ToString(),
            StatusName = GetStatusName(booking.Status),
            Customer = new Response.CustomerResponse
            {
                CustomerId = booking.Customer.CustomerId,
                FullName = booking.Customer.FullName,
                Phone = booking.Customer.Phone
            },
            OriginAddress = booking.OriginAddress,
            OriginCountry = booking.OriginCountry,
            DestinationAddress = booking.DestinationAddress,
            DestinationCountry = booking.DestinationCountry,
            RequestedDepartureDate = booking.RequestedDepartureDate,
            RequestedArrivalDate = booking.RequestedArrivalDate,
            TotalHorses = booking.BookingHorses.Count,
            Horses = booking.BookingHorses.Select(item => new Response.BookingHorseResponse
            {
                HorseId = item.HorseId,
                HorseName = item.Horse.HorseName,
                Sex = item.Horse.Sex?.ToString(),
                Breed = item.Horse.Breed,
                MicrochipNumber = item.Horse.MicrochipNumber,
                PassportNumber = item.Horse.PassportNumber,
                SpecialHandlingNotes = item.SpecialHandlingNotes
            }).ToList(),
            SpecialRequirements = booking.SpecialRequirements,
            CustomerNote = booking.CustomerNote,
            AssignedEmployee = booking.AssignedEmployee is null
                ? null
                : new Response.AssignedEmployeeResponse
                {
                    EmployeeId = booking.AssignedEmployee.EmployeeId,
                    FullName = booking.AssignedEmployee.FullName
                },
            SubmittedAt = AsUtc(booking.SubmittedAt ?? booking.CreatedAt),
            ApprovedAt = AsUtcNullable(booking.ApprovedAt),
            RejectedAt = AsUtcNullable(booking.RejectedAt),
            CancelledAt = AsUtcNullable(booking.CancelledAt),
            RejectionReason = booking.RejectionReason,
            CancellationReason = booking.CancellationReason,
            Transport = booking.Transport is null
                ? null
                : new Response.TransportDetailResponse
                {
                    TransportId = booking.Transport.TransportId,
                    TransportCode = booking.Transport.TransportCode,
                    Status = booking.Transport.Status.ToString()
                },
            CreatedAt = AsUtc(booking.CreatedAt),
            UpdatedAt = AsUtc(booking.UpdatedAt)
        };
    }

    public async Task<Response.ApprovedBookingDetailResponse> GetApprovedAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees
            .Where(item => item.AccountId == accountId &&
                !item.IsDeleted && item.IsActive &&
                !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
            .Select(item => new { item.EmployeeId, item.Role })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            throw new ForbiddenException("Only Logistics Managers, Transport Specialists and Fleet Route Coordinators can access approved bookings");

        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(item => item.Customer)
            .Include(item => item.BookingHorses)
                .ThenInclude(item => item.Horse)
            .Include(item => item.Transport)
                .ThenInclude(item => item!.ComplianceDossier)
            .Include(item => item.Transport)
                .ThenInclude(item => item!.TransportPlans)
                    .ThenInclude(item => item.EmployeeTransportPlans)
                        .ThenInclude(item => item.Employee)
            .Include(item => item.Transport)
                .ThenInclude(item => item!.TransportPlans)
                    .ThenInclude(item => item.RoutePlans)
                        .ThenInclude(item => item.RoutePlanRoutes)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.APPROVED)
            throw new ConflictException(
                $"This action cannot be performed while the booking is in {GetStatusName(booking.Status)} status",
                "INVALID_BOOKING_STATUS");

        var transportPlan = booking.Transport?.TransportPlans
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        if (employee.Role is EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR &&
            (transportPlan is null || transportPlan.Status != TransportPlanStatus.APPROVED ||
             !transportPlan.EmployeeTransportPlans.Any(item => item.EmployeeId == employee.EmployeeId)))
            throw new ForbiddenException(
                "You are not assigned to this Transport Plan or the Transport Plan has not been approved",
                "FORBIDDEN");

        return new Response.ApprovedBookingDetailResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Status = booking.Status.ToString(),
            Customer = new Response.CustomerResponse
            {
                CustomerId = booking.Customer.CustomerId,
                CustomerCode = null,
                FullName = booking.Customer.FullName,
                Phone = booking.Customer.Phone
            },
            OriginAddress = booking.OriginAddress,
            OriginCountry = booking.OriginCountry,
            DestinationAddress = booking.DestinationAddress,
            DestinationCountry = booking.DestinationCountry,
            IsInternational = booking.OriginCountry != booking.DestinationCountry,
            RequestedDepartureDate = booking.RequestedDepartureDate,
            RequestedArrivalDate = booking.RequestedArrivalDate,
            SpecialRequirements = booking.SpecialRequirements,
            CustomerNote = booking.CustomerNote,
            TotalHorses = booking.BookingHorses.Count,
            Horses = booking.BookingHorses.Select(item => new Response.BookingHorseResponse
            {
                HorseId = item.HorseId,
                HorseName = item.Horse.HorseName,
                Sex = item.Horse.Sex?.ToString(),
                Breed = item.Horse.Breed,
                MicrochipNumber = item.Horse.MicrochipNumber,
                PassportNumber = item.Horse.PassportNumber,
                SpecialHandlingNotes = item.SpecialHandlingNotes
            }).ToList(),
            Transport = booking.Transport is null
                ? null
                : new Response.TransportDetailResponse
                {
                    TransportId = booking.Transport.TransportId,
                    TransportCode = booking.Transport.TransportCode,
                    Status = booking.Transport.Status.ToString()
                },
            TransportPlan = transportPlan is null
                ? null
                : new Response.TransportPlanSummary
                {
                    TransportPlanId = transportPlan.TransportPlanId,
                    PlanCode = transportPlan.PlanCode,
                    Status = transportPlan.Status.ToString(),
                    PlannedStartAt = AsUtcNullable(transportPlan.PlannedStartAt),
                    PlannedEndAt = AsUtcNullable(transportPlan.PlannedEndAt),
                    EstimatedDistanceKm = transportPlan.EstimatedDistanceKm,
                    EstimatedDurationMinutes = transportPlan.EstimatedDurationMinutes,
                    PlanningNote = transportPlan.PlanningNote
                },
            ComplianceDossier = booking.Transport?.ComplianceDossier is null
                ? null
                : new Response.ComplianceDossierSummary
                {
                    ComplianceDossierId = booking.Transport.ComplianceDossier.ComplianceDossierId,
                    Status = booking.Transport.ComplianceDossier.Status.ToString()
                },
            RoutePlans = transportPlan?.RoutePlans
                .OrderBy(item => item.CreatedAt)
                .Select(item => new Response.ApprovedRoutePlanResponse
                {
                    RoutePlanId = item.RoutePlanId,
                    RoutePlanCode = item.RoutePlanCode,
                    RouteType = item.RoutePlanRoutes
                        .OrderBy(route => route.SequenceNo)
                        .Select(route => route.RouteType.ToString())
                        .FirstOrDefault() ?? RouteType.PRIMARY.ToString(),
                    Status = item.Status.ToString()
                })
                .ToList() ?? []
        };
    }

    public async Task<IReadOnlyList<Response.BookingHistoryResponse>> GetHistoriesAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        var bookingExists = await _db.Bookings.AnyAsync(booking =>
            booking.BookingId == bookingId &&
            (canViewAll && booking.Status != BookingStatus.DRAFT ||
             !canViewAll && booking.Customer.AccountId == accountId),
            cancellationToken);
        if (!bookingExists)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        var histories = await _db.BookingStatusHistories
            .AsNoTracking()
            .Where(history => history.BookingId == bookingId)
            .Include(history => history.ChangedByAccount)
                .ThenInclude(account => account.CustomerAccount)
            .Include(history => history.ChangedByAccount)
                .ThenInclude(account => account.EmployeeAccount)
            .OrderBy(history => history.ChangedAt)
            .ThenBy(history => history.BookingStatusHistoryId)
            .ToListAsync(cancellationToken);

        return histories.Select(history => new Response.BookingHistoryResponse
        {
            HistoryId = history.BookingStatusHistoryId,
            Action = GetHistoryAction(history),
            PreviousStatus = history.PreviousStatus?.ToString(),
            NewStatus = history.NewStatus.ToString(),
            Reason = history.Reason,
            ChangedBy = new Response.ChangedByResponse
            {
                AccountId = history.ChangedByAccountId,
                FullName = GetAccountFullName(history.ChangedByAccount),
                Role = GetAccountRole(history.ChangedByAccount)
            },
            ChangedAt = AsUtc(history.ChangedAt)
        }).ToList();
    }

    public async Task<Response.BookingListResponse> GetListAsync(
        Guid accountId,
        Request.BookingListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1)
            throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (request.PageSize < 1 || request.PageSize > 100)
            throw new BadRequestException("PageSize must be between 1 and 100");

        var employeeRole = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);

        var customerId = await _db.Customers
            .Where(customer =>
                customer.AccountId == accountId &&
                !customer.IsDeleted &&
                !customer.Account.IsDeleted &&
                customer.Account.Status == AccountStatus.ACTIVE)
            .Select(customer => (Guid?)customer.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);

        var canViewAll = employeeRole == EmployeeRole.LOGISTICS_MANAGER;
        if (!canViewAll && customerId is null)
            throw new ForbiddenException("Only customers and logistics managers can access bookings");

        var query = _db.Bookings
            .AsNoTracking()
            .Where(booking =>
                (canViewAll && booking.Status != BookingStatus.DRAFT) ||
                (!canViewAll && booking.CustomerId == customerId!.Value));

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<BookingStatus>(request.Status.Trim(), true, out var status))
                throw new BadRequestException("Booking status is invalid", "INVALID_STATUS");
            query = query.Where(booking => booking.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = canViewAll
                ? query.Where(booking =>
                    booking.BookingCode.ToLower().Contains(search) ||
                    booking.Customer.FullName.ToLower().Contains(search) ||
                    booking.OriginAddress.ToLower().Contains(search) ||
                    booking.OriginCountry.ToLower().Contains(search) ||
                    booking.DestinationAddress.ToLower().Contains(search) ||
                    booking.DestinationCountry.ToLower().Contains(search))
                : query.Where(booking =>
                    booking.BookingCode.ToLower().Contains(search) ||
                    booking.OriginAddress.ToLower().Contains(search) ||
                    booking.OriginCountry.ToLower().Contains(search) ||
                    booking.DestinationAddress.ToLower().Contains(search) ||
                    booking.DestinationCountry.ToLower().Contains(search));
        }

        query = ApplySorting(query, request.SortBy);

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(booking => new Response.BookingListItemResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                StatusName = GetStatusName(booking.Status),
                CustomerName = booking.Customer.FullName,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                TotalHorses = booking.BookingHorses.Count,
                CreatedAt = AsUtc(booking.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        return new Response.BookingListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    public async Task<Response.ApprovedBookingListResponse> GetApprovedListAsync(
        Guid accountId,
        Request.ApprovedBookingListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PageIndex < 1)
            throw new BadRequestException("PageIndex must be greater than or equal to 1");
        if (request.PageSize < 1 || request.PageSize > 100)
            throw new BadRequestException("PageSize must be between 1 and 100");
        if (request.DepartureFrom.HasValue && request.DepartureTo.HasValue &&
            request.DepartureFrom > request.DepartureTo)
            throw new BadRequestException("departureFrom must not be after departureTo");

        var role = await _db.Employees
            .Where(employee => employee.AccountId == accountId && !employee.IsDeleted && employee.IsActive &&
                !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (EmployeeRole?)employee.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not (EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.LOGISTICS_MANAGER))
            throw new ForbiddenException("Only Logistics Managers, Transport Specialists and Fleet Route Coordinators can access approved bookings");

        var query = _db.Bookings
            .AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.APPROVED);

        if (role is EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.FLEET_ROUTE_COORDINATOR)
        {
            query = query.Where(booking => booking.Transport != null &&
                booking.Transport.TransportPlans.Any(plan =>
                    plan.CreatedAt == booking.Transport.TransportPlans.Max(current => current.CreatedAt) &&
                    plan.EmployeeTransportPlans.Any(assignment => assignment.Employee.AccountId == accountId)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(booking =>
                booking.BookingCode.ToLower().Contains(search) ||
                booking.Customer.FullName.ToLower().Contains(search));
        }

        if (request.IsInternational.HasValue)
            query = query.Where(booking =>
                (booking.OriginCountry != booking.DestinationCountry) == request.IsInternational.Value);

        if (request.DepartureFrom.HasValue)
            query = query.Where(booking => booking.RequestedDepartureDate >= request.DepartureFrom.Value);
        if (request.DepartureTo.HasValue)
            query = query.Where(booking => booking.RequestedDepartureDate <= request.DepartureTo.Value);

        if (request.HasTransportPlan.HasValue)
            query = request.HasTransportPlan.Value
                ? query.Where(booking => booking.Transport != null && booking.Transport.TransportPlans.Any())
                : query.Where(booking => booking.Transport == null || !booking.Transport.TransportPlans.Any());

        if (!string.IsNullOrWhiteSpace(request.TransportPlanStatus))
        {
            if (!Enum.TryParse<TransportPlanStatus>(request.TransportPlanStatus.Trim(), true, out var status))
                throw new BadRequestException("Transport plan status is invalid", "INVALID_TRANSPORT_PLAN_STATUS");
            query = query.Where(booking => booking.Transport != null &&
                booking.Transport.TransportPlans
                    .OrderByDescending(plan => plan.CreatedAt)
                    .Select(plan => (TransportPlanStatus?)plan.Status)
                    .FirstOrDefault() == status);
        }

        if (request.HasRoutePlan.HasValue)
        {
            query = request.HasRoutePlan.Value
                ? query.Where(booking => booking.Transport != null && booking.Transport.TransportPlans.Any(plan =>
                    plan.CreatedAt == booking.Transport.TransportPlans.Max(current => current.CreatedAt) &&
                    plan.RoutePlans.Any(routePlan => routePlan.Status != RoutePlanStatus.CANCELLED &&
                        routePlan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.PRIMARY))))
                : query.Where(booking => booking.Transport == null || !booking.Transport.TransportPlans.Any(plan =>
                    plan.CreatedAt == booking.Transport.TransportPlans.Max(current => current.CreatedAt) &&
                    plan.RoutePlans.Any(routePlan => routePlan.Status != RoutePlanStatus.CANCELLED &&
                        routePlan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.PRIMARY))));
        }

        if (!string.IsNullOrWhiteSpace(request.RoutePlanStatus))
        {
            if (!Enum.TryParse<RoutePlanStatus>(request.RoutePlanStatus.Trim(), true, out var status))
                throw new BadRequestException("Route plan status is invalid", "INVALID_ROUTE_PLAN_STATUS");
            query = query.Where(booking => booking.Transport != null && booking.Transport.TransportPlans.Any(plan =>
                plan.CreatedAt == booking.Transport.TransportPlans.Max(current => current.CreatedAt) &&
                plan.RoutePlans.Any(routePlan => routePlan.Status == status && routePlan.Status != RoutePlanStatus.CANCELLED &&
                    routePlan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.PRIMARY))));
        }

        if (request.HasComplianceDossier.HasValue)
            query = request.HasComplianceDossier.Value
                ? query.Where(booking => booking.Transport != null && booking.Transport.ComplianceDossier != null)
                : query.Where(booking => booking.Transport == null || booking.Transport.ComplianceDossier == null);

        if (!string.IsNullOrWhiteSpace(request.ComplianceDossierStatus))
        {
            if (!Enum.TryParse<ComplianceDossierStatus>(request.ComplianceDossierStatus.Trim(), true, out var status))
                throw new BadRequestException("Compliance dossier status is invalid", "INVALID_COMPLIANCE_DOSSIER_STATUS");
            query = query.Where(booking => booking.Transport != null &&
                booking.Transport.ComplianceDossier != null &&
                booking.Transport.ComplianceDossier.Status == status);
        }

        query = ApplyApprovedBookingSorting(query, request.SortBy);
        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(booking => new Response.ApprovedBookingListItemResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status.ToString(),
                CustomerName = booking.Customer.FullName,
                CustomerCode = null,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                IsInternational = booking.OriginCountry != booking.DestinationCountry,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                RequestedArrivalDate = booking.RequestedArrivalDate,
                SpecialRequirements = booking.SpecialRequirements,
                ApprovedAt = AsUtcNullable(booking.ApprovedAt),
                TotalHorses = booking.BookingHorses.Count,
                TransportId = booking.Transport == null ? null : booking.Transport.TransportId,
                CreatedAt = AsUtc(booking.CreatedAt),
                TransportPlan = booking.Transport == null
                    ? null
                    : booking.Transport.TransportPlans
                        .OrderByDescending(plan => plan.CreatedAt)
                        .Select(plan => new Response.TransportPlanSummary
                        {
                            TransportPlanId = plan.TransportPlanId,
                            PlanCode = plan.PlanCode,
                            Status = plan.Status.ToString(),
                            PlannedStartAt = AsUtcNullable(plan.PlannedStartAt),
                            PlannedEndAt = AsUtcNullable(plan.PlannedEndAt),
                            EstimatedDistanceKm = plan.EstimatedDistanceKm,
                            EstimatedDurationMinutes = plan.EstimatedDurationMinutes,
                            PlanningNote = plan.PlanningNote
                        })
                        .FirstOrDefault(),
                ComplianceDossier = booking.Transport != null && booking.Transport.ComplianceDossier != null
                    ? new Response.ComplianceDossierSummary
                    {
                        ComplianceDossierId = booking.Transport.ComplianceDossier.ComplianceDossierId,
                        Status = booking.Transport.ComplianceDossier.Status.ToString()
                    }
                    : null,
                RoutePlan = booking.Transport == null
                    ? null
                    : booking.Transport.TransportPlans
                        .OrderByDescending(plan => plan.CreatedAt)
                        .SelectMany(plan => plan.RoutePlans
                            .Where(routePlan => routePlan.Status != RoutePlanStatus.CANCELLED &&
                                routePlan.RoutePlanRoutes.Any(route => route.RouteType == RouteType.PRIMARY))
                            .OrderByDescending(routePlan => routePlan.CreatedAt)
                            .Select(routePlan => new Response.RoutePlanSummary
                            {
                                RoutePlanId = routePlan.RoutePlanId,
                                RoutePlanCode = routePlan.RoutePlanCode,
                                Status = routePlan.Status.ToString()
                            }))
                        .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return new Response.ApprovedBookingListResponse
        {
            Items = items,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize)
        };
    }

    private static IOrderedQueryable<BookingEntity> ApplyApprovedBookingSorting(
        IQueryable<BookingEntity> query,
        string? sortBy)
    {
        var value = string.IsNullOrWhiteSpace(sortBy)
            ? "requestedDepartureDate:asc"
            : sortBy.Trim();
        var parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            (parts[1].ToLowerInvariant() is not ("asc" or "desc")) ||
            (parts[0].ToLowerInvariant() is not ("requesteddeparturedate" or "approvedat" or "createdat")))
            throw new BadRequestException("The sort parameter is invalid", "INVALID_SORT_PARAMETER");

        var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        return parts[0].ToLowerInvariant() switch
        {
            "approvedat" when ascending => query.OrderBy(booking => booking.ApprovedAt),
            "approvedat" => query.OrderByDescending(booking => booking.ApprovedAt),
            "createdat" when ascending => query.OrderBy(booking => booking.CreatedAt),
            "createdat" => query.OrderByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.OrderBy(booking => booking.RequestedDepartureDate),
            _ => query.OrderByDescending(booking => booking.RequestedDepartureDate)
        };
    }

    public async Task AddHorseIntoBookingAsync(
        Guid accountId,
        Guid bookingId,
        Request.AddHorseRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var horse = await _db.Horses.SingleOrDefaultAsync(item =>
            item.HorseId == request.HorseId &&
            item.CustomerId == booking.CustomerId &&
            !item.IsDeleted,
            cancellationToken);
        if (horse is null)
            throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");

        if (await _db.BookingHorses.AnyAsync(item =>
                item.BookingId == bookingId && item.HorseId == request.HorseId,
                cancellationToken))
            throw new ConflictException("Horse already belongs to this booking", "HORSE_ALREADY_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Add(new BookingHorse
        {
            BookingId = bookingId,
            HorseId = request.HorseId,
            SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes),
            CreatedAt = now
        });
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Added horse: {horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateHorseNotesAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        Request.UpdateHorseNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        if (request.SpecialHandlingNotes?.Length > 2000)
            throw new BadRequestException("Special handling notes must not exceed 2000 characters");

        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        bookingHorse.SpecialHandlingNotes = EmptyToNull(request.SpecialHandlingNotes);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Updated handling notes for horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveHorseAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var bookingHorse = await _db.BookingHorses
            .Include(item => item.Horse)
            .SingleOrDefaultAsync(item =>
                item.BookingId == bookingId &&
                item.HorseId == horseId,
                cancellationToken);
        if (bookingHorse is null)
            throw new NotFoundException("Horse is not in this booking", "HORSE_NOT_IN_BOOKING");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.BookingHorses.Remove(bookingHorse);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Removed horse: {bookingHorse.Horse.HorseName}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid accountId,
        Guid bookingId,
        Request.ChangeStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.CustomerId == customer.CustomerId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");

        if (!Enum.TryParse<BookingStatus>(request.Status.Trim(), true, out var targetStatus) ||
            targetStatus is not (BookingStatus.DRAFT or BookingStatus.PENDING))
            throw new BadRequestException("Only DRAFT and PENDING statuses are supported", "INVALID_BOOKING_STATUS");

        var validTransition = booking.Status == BookingStatus.PENDING && targetStatus == BookingStatus.DRAFT ||
                              booking.Status == BookingStatus.DRAFT && targetStatus == BookingStatus.PENDING;
        if (!validTransition)
            throw new ConflictException(
                "The booking status cannot be changed from its current state",
                "INVALID_BOOKING_STATUS");
        if (targetStatus == BookingStatus.PENDING && !await HasHorseAsync(bookingId, cancellationToken))
            throw new UnprocessableEntityException(
                "The booking has no horse and cannot be submitted",
                "BOOKING_HAS_NO_HORSE");

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var previousStatus = booking.Status;
        booking.Status = targetStatus;
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            previousStatus,
            targetStatus,
            targetStatus == BookingStatus.DRAFT
                ? "Booking moved to draft"
                : "Booking submitted for review",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Response.CreateBookingResponse> CreateAsync(
        Guid accountId,
        Request.CreateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDates(request.RequestedDepartureDate, request.RequestedArrivalDate);
        if (request.Horses is null || request.Horses.Count == 0)
            throw new BadRequestException("At least one horse is required", "BOOKING_HAS_NO_HORSE");
        if (request.Horses.Select(horse => horse.HorseId).Distinct().Count() != request.Horses.Count)
            throw new BadRequestException(
                "A horse can only be selected once per booking",
                "DUPLICATE_HORSE_IN_BOOKING");

        var customer = await GetCustomerAsync(accountId, cancellationToken);
        var horseIds = request.Horses.Select(horse => horse.HorseId).ToArray();
        if (horseIds.Length > 0)
        {
            var ownedHorseCount = await _db.Horses.CountAsync(horse =>
                horseIds.Contains(horse.HorseId) &&
                horse.CustomerId == customer.CustomerId &&
                !horse.IsDeleted,
                cancellationToken);
            if (ownedHorseCount != horseIds.Length)
                throw new NotFoundException("Horse not found", "HORSE_NOT_FOUND");
        }

        var (isRouteValid, routeErrorMessage) = CountryUtils.ValidateRoute(request.OriginCountry, request.DestinationCountry);
        if (!isRouteValid)
            throw new BadRequestException(routeErrorMessage);

        var now = UtcNow();
        var booking = new BookingEntity
        {
            BookingId = Guid.NewGuid(),
            BookingCode = await GenerateBookingCodeAsync(cancellationToken),
            CustomerId = customer.CustomerId,
            Status = BookingStatus.PENDING,
            OriginAddress = request.OriginAddress.Trim(),
            OriginCountry = request.OriginCountry.Trim(),
            DestinationAddress = request.DestinationAddress.Trim(),
            DestinationCountry = request.DestinationCountry.Trim(),
            RequestedDepartureDate = request.RequestedDepartureDate,
            RequestedArrivalDate = request.RequestedArrivalDate,
            SpecialRequirements = EmptyToNull(request.SpecialRequirements),
            CustomerNote = EmptyToNull(request.CustomerNote),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Bookings.Add(booking);
        _db.BookingHorses.AddRange(request.Horses.Select(horse => new BookingHorse
        {
            BookingId = booking.BookingId,
            HorseId = horse.HorseId,
            SpecialHandlingNotes = EmptyToNull(horse.SpecialHandlingNotes),
            CreatedAt = now
        }));
        AddHistory(booking.BookingId, accountId, BookingHistoryType.SUBMIT, null, BookingStatus.PENDING, null, now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new Response.CreateBookingResponse
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode
        };
    }

    public async Task<Response.UpdateBookingResponse> UpdateAsync(
        Guid accountId,
        Guid bookingId,
        Request.UpdateBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedDraftBookingAsync(accountId, bookingId, cancellationToken);
        var clientUpdatedAtUtc = ToDatabasePrecision(request.LastUpdatedAt.UtcDateTime);
        var clientUpdatedAtValue = ToDatabasePrecision(request.LastUpdatedAt.DateTime);
        if (booking.UpdatedAt != clientUpdatedAtUtc && booking.UpdatedAt != clientUpdatedAtValue)
            throw new ConflictException(
                "The booking was modified. Refresh and try again",
                "BOOKING_MODIFIED");

        var departureDate = request.RequestedDepartureDate ?? booking.RequestedDepartureDate;
        var arrivalDate = request.RequestedArrivalDate ?? booking.RequestedArrivalDate;
        ValidateDates(departureDate, arrivalDate);

        var targetOrigin = request.OriginCountry?.Trim() ?? booking.OriginCountry;
        var targetDestination = request.DestinationCountry?.Trim() ?? booking.DestinationCountry;
        var (isUpdatedRouteValid, updatedRouteErrorMessage) = CountryUtils.ValidateRoute(targetOrigin, targetDestination);
        if (!isUpdatedRouteValid)
            throw new BadRequestException(updatedRouteErrorMessage);

        var changedFields = new List<string>();
        Set(request.OriginAddress, value => booking.OriginAddress = value, "originAddress", changedFields);
        Set(request.OriginCountry, value => booking.OriginCountry = value, "originCountry", changedFields);
        Set(request.DestinationAddress, value => booking.DestinationAddress = value, "destinationAddress", changedFields);
        Set(request.DestinationCountry, value => booking.DestinationCountry = value, "destinationCountry", changedFields);
        if (request.RequestedDepartureDate.HasValue)
        {
            booking.RequestedDepartureDate = request.RequestedDepartureDate.Value;
            changedFields.Add("requestedDepartureDate");
        }
        if (request.RequestedArrivalDate.HasValue)
        {
            booking.RequestedArrivalDate = request.RequestedArrivalDate;
            changedFields.Add("requestedArrivalDate");
        }
        SetOptional(request.SpecialRequirements, value => booking.SpecialRequirements = value, "specialRequirements", changedFields);
        SetOptional(request.CustomerNote, value => booking.CustomerNote = value, "customerNote", changedFields);

        var now = UtcNow();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.UpdatedAt = now;
        AddHistory(
            bookingId,
            accountId,
            BookingHistoryType.SUBMIT,
            BookingStatus.DRAFT,
            BookingStatus.DRAFT,
            $"Updated: {string.Join(", ", changedFields)}",
            now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Response.UpdateBookingResponse { UpdatedAt = AsUtc(booking.UpdatedAt) };
    }

    public async Task CancelAsync(
        Guid accountId,
        Guid bookingId,
        Request.CancelBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var booking = await GetOwnedPendingBookingAsync(accountId, bookingId, cancellationToken);
        var now = UtcNow();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.CANCELLED;
        booking.CancelledAt = now;
        booking.CancellationReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.CANCEL, BookingStatus.PENDING, BookingStatus.CANCELLED, reason, now);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Response.ApproveBookingResponse> ApproveAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");
        if (!await HasHorseAsync(bookingId, cancellationToken))
            throw new UnprocessableEntityException(
                "The booking has no horse and cannot be approved",
                "BOOKING_HAS_NO_HORSE");

        var now = UtcNow();
        var transport = new Transport
        {
            TransportId = Guid.NewGuid(),
            TransportCode = await GenerateTransportCodeAsync(cancellationToken),
            BookingId = bookingId,
            Status = TransportStatus.CREATED,
            CreatedAt = now,
            UpdatedAt = now
        };
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.APPROVED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.ApprovedAt = now;
        booking.UpdatedAt = now;
        _db.Transports.Add(transport);
        AddHistory(bookingId, accountId, BookingHistoryType.APPROVE, BookingStatus.PENDING, BookingStatus.APPROVED, null, now);
        AddNotification(booking.Customer.AccountId, transport.TransportId, booking, "Booking approved", now);
        var approvalMail = new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = "Booking approved",
            Body = $"<p>Booking <strong>{WebUtility.HtmlEncode(booking.BookingCode)}</strong> has been approved.</p>"
        };
        if (OutboxQueue.IsEnabled(_db))
            OutboxQueue.AddEmail(_db, approvalMail, now);
        await _db.SaveChangesAsync(cancellationToken);
        if (!OutboxQueue.IsEnabled(_db))
            await _mailService.SendMail(approvalMail, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return new Response.ApproveBookingResponse
        {
            Status = booking.Status,
            Transport = new Response.TransportResponse
            {
                TransportId = transport.TransportId,
                TransportCode = transport.TransportCode,
                Status = transport.Status
            }
        };
    }

    public async Task RejectAsync(
        Guid accountId,
        Guid bookingId,
        Request.RejectBookingRequest request,
        CancellationToken cancellationToken = default)
    {
        var employee = await GetLogisticsManagerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings
            .Include(item => item.Customer).ThenInclude(customer => customer.Account)
            .Include(item => item.Transport)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken);
        EnsurePending(booking);
        if (booking!.Transport is not null)
            throw new ConflictException("The booking already has a transport", "INVALID_BOOKING_STATUS");

        var now = UtcNow();
        var reason = request.Reason.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        booking.Status = BookingStatus.REJECTED;
        booking.AssignedEmployeeId = employee.EmployeeId;
        booking.RejectedAt = now;
        booking.RejectionReason = reason;
        booking.UpdatedAt = now;
        AddHistory(bookingId, accountId, BookingHistoryType.REJECT, BookingStatus.PENDING, BookingStatus.REJECTED, reason, now);
        AddNotification(booking.Customer.AccountId, null, booking, "Booking rejected", now);
        var rejectionMail = new MailService.MailContent
        {
            To = booking.Customer.Account.Email,
            Subject = "Booking rejected",
            Body = $"<p>Booking <strong>{WebUtility.HtmlEncode(booking.BookingCode)}</strong> has been rejected.</p><p>Reason: {WebUtility.HtmlEncode(reason)}</p>"
        };
        if (OutboxQueue.IsEnabled(_db))
            OutboxQueue.AddEmail(_db, rejectionMail, now);
        await _db.SaveChangesAsync(cancellationToken);
        if (!OutboxQueue.IsEnabled(_db))
            await _mailService.SendMail(rejectionMail, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
    }

    private async Task<Customer> GetCustomerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            !item.IsDeleted &&
            !item.Account.IsDeleted &&
            item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        return customer ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private async Task<BookingEntity> GetOwnedPendingBookingAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.Customer.AccountId == accountId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
        return booking;
    }

    private async Task<BookingEntity> GetOwnedDraftBookingAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var booking = await _db.Bookings.SingleOrDefaultAsync(item =>
            item.BookingId == bookingId && item.Customer.AccountId == accountId,
            cancellationToken);
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.DRAFT)
            throw new ConflictException("The booking must be in DRAFT status to be edited", "INVALID_BOOKING_STATUS");
        return booking;
    }

    private async Task<Employee> GetLogisticsManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.SingleOrDefaultAsync(item =>
            item.AccountId == accountId &&
            item.Role == EmployeeRole.LOGISTICS_MANAGER &&
            item.IsActive &&
            !item.IsDeleted &&
            !item.Account.IsDeleted &&
            item.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        return employee ?? throw new ForbiddenException("You do not have permission to perform this action");
    }

    private static void EnsurePending(BookingEntity? booking)
    {
        if (booking is null)
            throw new NotFoundException("Booking not found", "BOOKING_NOT_FOUND");
        if (booking.Status != BookingStatus.PENDING)
            throw new ConflictException("The action is not allowed in the current booking status", "INVALID_BOOKING_STATUS");
    }

    private static IQueryable<BookingEntity> ApplySorting(
        IQueryable<BookingEntity> query,
        IReadOnlyCollection<string>? sortBy)
    {
        var sortParameters = sortBy is null || sortBy.Count == 0
            ? ["createdAt:desc"]
            : sortBy.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        IOrderedQueryable<BookingEntity>? orderedQuery = null;
        foreach (var sortParameter in sortParameters)
        {
            var parts = sortParameter.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                (parts[1].ToLowerInvariant() != "asc" && parts[1].ToLowerInvariant() != "desc") ||
                (parts[0].ToLowerInvariant() != "createdat" &&
                 parts[0].ToLowerInvariant() != "requesteddeparturedate"))
                throw new BadRequestException("Sort parameter is invalid", "INVALID_SORT_PARAMETER");

            var ascending = parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
            var field = parts[0].ToLowerInvariant();
            orderedQuery = orderedQuery is null
                ? ApplyPrimarySort(query, field, ascending)
                : ApplySecondarySort(orderedQuery, field, ascending);
        }

        return orderedQuery ?? query.OrderByDescending(booking => booking.CreatedAt);
    }

    private static IOrderedQueryable<BookingEntity> ApplyPrimarySort(
        IQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.OrderBy(booking => booking.CreatedAt),
            "createdat" => query.OrderByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.OrderBy(booking => booking.RequestedDepartureDate),
            _ => query.OrderByDescending(booking => booking.RequestedDepartureDate)
        };

    private static IOrderedQueryable<BookingEntity> ApplySecondarySort(
        IOrderedQueryable<BookingEntity> query,
        string field,
        bool ascending) => field switch
        {
            "createdat" when ascending => query.ThenBy(booking => booking.CreatedAt),
            "createdat" => query.ThenByDescending(booking => booking.CreatedAt),
            "requesteddeparturedate" when ascending => query.ThenBy(booking => booking.RequestedDepartureDate),
            _ => query.ThenByDescending(booking => booking.RequestedDepartureDate)
        };

    private static string GetStatusName(BookingStatus status) => status switch
    {
        BookingStatus.DRAFT => "Draft",
        BookingStatus.PENDING => "Pending",
        BookingStatus.APPROVED => "Approved",
        BookingStatus.REJECTED => "Rejected",
        BookingStatus.CANCELLED => "Cancelled",
        _ => status.ToString()
    };

    private static string GetHistoryAction(BookingStatusHistory history) => history.Type switch
    {
        BookingHistoryType.APPROVE => "APPROVED",
        BookingHistoryType.REJECT => "REJECTED",
        BookingHistoryType.CANCEL => "CANCELLED",
        BookingHistoryType.SUBMIT when history.PreviousStatus is null => "CREATED",
        BookingHistoryType.SUBMIT => "UPDATED",
        _ => history.Type.ToString()
    };

    private static string GetAccountFullName(Account account) =>
        account.CustomerAccount?.FullName ?? account.EmployeeAccount?.FullName ?? account.Email;

    private static string GetAccountRole(Account account) => account.AccountType == AccountType.CUSTOMER
        ? "CUSTOMER"
        : account.EmployeeAccount?.Role.ToString() ?? "EMPLOYEE";

    private void AddHistory(
        Guid bookingId,
        Guid accountId,
        BookingHistoryType type,
        BookingStatus? previousStatus,
        BookingStatus newStatus,
        string? reason,
        DateTime changedAt) => _db.BookingStatusHistories.Add(new BookingStatusHistory
    {
        BookingStatusHistoryId = Guid.NewGuid(),
        BookingId = bookingId,
        Type = type,
        PreviousStatus = previousStatus,
        NewStatus = newStatus,
        ChangedByAccountId = accountId,
        Reason = reason,
        ChangedAt = changedAt
    });

    private void AddNotification(Guid accountId, Guid? transportId, BookingEntity booking, string title, DateTime now)
    {
        _notificationService.Queue(new NotificationService.NotificationCreateRequest
        {
            AccountId = accountId,
            TransportId = transportId,
            Type = NotificationType.BOOKING,
            Priority = NotificationPriority.NORMAL,
            Title = title,
            Message = $"Booking {booking.BookingCode}: {title.ToLowerInvariant()}.",
            ReferenceType = "Booking",
            ReferenceId = booking.BookingId,
            ActionUrl = $"/bookings/{booking.BookingId}",
            CreatedAt = now
        });
    }

    private async Task<string> GenerateBookingCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("REQ", code => _db.Bookings.AnyAsync(booking => booking.BookingCode == code, cancellationToken));

    private Task<bool> HasHorseAsync(Guid bookingId, CancellationToken cancellationToken) =>
        _db.BookingHorses.AnyAsync(item => item.BookingId == bookingId && !item.Horse.IsDeleted, cancellationToken);

    private async Task<string> GenerateTransportCodeAsync(CancellationToken cancellationToken) =>
        await GenerateCodeAsync("TP", code => _db.Transports.AnyAsync(transport => transport.TransportCode == code, cancellationToken));

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

    private static void ValidateDates(DateOnly departureDate, DateOnly? arrivalDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (departureDate < today.AddDays(21) || departureDate > today.AddMonths(2))
            throw new BadRequestException(
                "The departure date must be between 3 weeks and 2 months from today",
                "DEPARTURE_DATE_OUT_OF_RANGE");
        if (arrivalDate.HasValue && arrivalDate.Value < departureDate)
            throw new BadRequestException(
                "The arrival date cannot be before the departure date",
                "ARRIVAL_DATE_INVALID");
    }

    private static void Set(string? value, Action<string> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(value.Trim());
        changedFields.Add(field);
    }

    private static void SetOptional(string? value, Action<string?> setter, string field, ICollection<string> changedFields)
    {
        if (value is null) return;
        setter(EmptyToNull(value));
        changedFields.Add(field);
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
