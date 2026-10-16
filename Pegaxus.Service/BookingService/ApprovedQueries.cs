using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

using BookingEntity = Pegaxus.Repository.Entities.Booking;

namespace Pegaxus.Service.BookingService;

public sealed partial class Service
{
    public async Task<Response.ApprovedBookingDetailResponse> GetApprovedAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var employee = await _db.Employees
            .Active()
            .Where(item => item.AccountId == accountId)
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
                .ThenInclude(item => item!.ComplianceDossiers)
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
                CustomerCode = booking.Customer.CustomerCode,
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
                    PlannedStartAt = AsUtc(transportPlan.PlannedStartAt),
                    PlannedEndAt = AsUtc(transportPlan.PlannedEndAt),
                    EstimatedDistanceKm = transportPlan.EstimatedDistanceKm,
                    EstimatedDurationMinutes = transportPlan.EstimatedDurationMinutes,
                    PlanningNote = transportPlan.PlanningNote
                },
            ComplianceDossier = booking.Transport?.MainDossier is not { } mainDossier
                ? null
                : new Response.ComplianceDossierSummary
                {
                    ComplianceDossierId = mainDossier.ComplianceDossierId,
                    Status = mainDossier.Status.ToString()
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
            .Active()
            .Where(employee => employee.AccountId == accountId)
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
                ? query.Where(booking => booking.Transport != null &&
                    booking.Transport.ComplianceDossiers.Any(dossier => dossier.DossierType == DossierType.MAIN))
                : query.Where(booking => booking.Transport == null ||
                    !booking.Transport.ComplianceDossiers.Any(dossier => dossier.DossierType == DossierType.MAIN));

        if (!string.IsNullOrWhiteSpace(request.ComplianceDossierStatus))
        {
            if (!Enum.TryParse<ComplianceDossierStatus>(request.ComplianceDossierStatus.Trim(), true, out var status))
                throw new BadRequestException("Compliance dossier status is invalid", "INVALID_COMPLIANCE_DOSSIER_STATUS");
            query = query.Where(booking => booking.Transport != null &&
                booking.Transport.ComplianceDossiers.Any(dossier => dossier.DossierType == DossierType.MAIN && dossier.Status == status));
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
                CustomerCode = booking.Customer.CustomerCode,
                OriginAddress = booking.OriginAddress,
                OriginCountry = booking.OriginCountry,
                DestinationAddress = booking.DestinationAddress,
                DestinationCountry = booking.DestinationCountry,
                IsInternational = booking.OriginCountry != booking.DestinationCountry,
                RequestedDepartureDate = booking.RequestedDepartureDate,
                RequestedArrivalDate = booking.RequestedArrivalDate,
                SpecialRequirements = booking.SpecialRequirements,
                ApprovedAt = AsUtc(booking.ApprovedAt),
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
                            PlannedStartAt = AsUtc(plan.PlannedStartAt),
                            PlannedEndAt = AsUtc(plan.PlannedEndAt),
                            EstimatedDistanceKm = plan.EstimatedDistanceKm,
                            EstimatedDurationMinutes = plan.EstimatedDurationMinutes,
                            PlanningNote = plan.PlanningNote
                        })
                        .FirstOrDefault(),
                ComplianceDossier = booking.Transport == null
                    ? null
                    : booking.Transport.ComplianceDossiers
                        .Where(dossier => dossier.DossierType == DossierType.MAIN)
                        .Select(dossier => new Response.ComplianceDossierSummary
                        {
                            ComplianceDossierId = dossier.ComplianceDossierId,
                            Status = dossier.Status.ToString()
                        })
                        .FirstOrDefault(),
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
            TotalPages = Paging.TotalPages(totalItems, request.PageSize)
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
}
