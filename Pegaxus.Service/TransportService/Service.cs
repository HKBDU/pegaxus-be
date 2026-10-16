using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.TransportService;

public sealed partial class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.ListRequest> listValidator, IValidator<Request.AssignedRequest> assignedValidator) : ITransportService
{
    private static readonly TransportStatus[] VisibleStatuses = [TransportStatus.READY, TransportStatus.IN_TRANSIT, TransportStatus.COMPLETED];
    private static readonly RoutePlanStatus[] VisiblePlanStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

    public async Task<Response.ListResponse> GetOngoingAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        if (!actor.IsBackOffice) throw new ForbiddenException("Only back-office employees can view ongoing transports", "FORBIDDEN");
        Validation.ValidateFields(request, listValidator);
        var statuses = ParseStatuses(request);
        var query = TransportQuery().Where(item => statuses.Contains(item.Status) && item.HasRoutePlan);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(item => item.TransportCode.ToLower().Contains(search) ||
                item.BookingCode.ToLower().Contains(search) || item.CustomerName.ToLower().Contains(search));
        }
        if (request.HasActiveIncident == true) query = query.Where(item => item.ActiveIncidentCount > 0);
        var total = await query.CountAsync(cancellationToken);
        var ordered = request.SortBy switch
        {
            "departureAt:desc" => query.OrderBy(item => item.DepartureAt == null).ThenByDescending(item => item.DepartureAt),
            "transportCode:asc" => query.OrderBy(item => item.TransportCode),
            _ => query.OrderBy(item => item.DepartureAt == null).ThenBy(item => item.DepartureAt)
        };
        var transports = await ordered.ThenBy(item => item.TransportId).Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize).ToListAsync(cancellationToken);
        var segments = await ReadSegmentsAsync(transports.Select(item => item.TransportId).ToArray(), actor, cancellationToken);
        var items = transports.Select(item =>
        {
            var effective = EffectiveSegments(segments.Where(segment => segment.TransportId == item.TransportId).ToList());
            var current = CurrentSegment(effective, item.Status);
            return new Response.OngoingItem(item.TransportId, item.TransportCode, item.BookingId, item.BookingCode,
                item.CustomerName, item.Status.ToString(), current?.Segment.RouteType, ToBrief(current, effective),
                item.HorseCount, item.ActiveIncidentCount, AsUtc(item.DepartureAt));
        }).ToList();
        return new(clock.GetUtcNow(), items, request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.AssignedResponse> GetAssignedAsync(Guid accountId, Request.AssignedRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(accountId, cancellationToken);
        if (!actor.IsCrew) throw new ForbiddenException("Only Drivers and Escorts can view assigned transports", "FORBIDDEN");
        Validation.ValidateFields(request, assignedValidator);
        var statuses = ParseStatuses(request);
        var assigned = AssignedSegments(actor);
        var transports = await TransportQuery().Where(item => statuses.Contains(item.Status) && item.HasRoutePlan &&
            assigned.Any(segment => segment.RoutePlan.TransportPlan.TransportId == item.TransportId))
            .OrderBy(item => item.DepartureAt == null).ThenBy(item => item.DepartureAt).ThenBy(item => item.TransportId)
            .ToListAsync(cancellationToken);
        var segments = await ReadSegmentsAsync(transports.Select(item => item.TransportId).ToArray(), actor, cancellationToken);
        var items = transports.Select(item =>
        {
            var effective = EffectiveSegments(segments.Where(segment => segment.TransportId == item.TransportId).ToList());
            return new Response.AssignedItem(item.TransportId, item.TransportCode, item.BookingCode, item.Status.ToString(),
                actor.Role == EmployeeRole.DRIVER ? "DRIVER" : "ESCORT", ToBrief(CurrentSegment(effective, item.Status), effective), item.HorseCount,
                AsUtc(item.DepartureAt), effective.LastOrDefault()?.Segment.PlannedArrivalAt);
        }).ToList();
        return new(clock.GetUtcNow(), items);
    }

    private async Task<Actor> GetActorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking().Where(item => item.AccountId == accountId &&
                !item.IsDeleted && item.Status == AccountStatus.ACTIVE)
            .Select(item => new
            {
                item.AccountType,
                CustomerId = item.CustomerAccount != null && !item.CustomerAccount.IsDeleted ? (Guid?)item.CustomerAccount.CustomerId : null,
                Employee = db.Employees.Active().Where(employee => employee.AccountId == item.AccountId)
                    .Select(employee => new { employee.EmployeeId, employee.Role }).FirstOrDefault()
            }).SingleOrDefaultAsync(cancellationToken);
        if (account is null)
            throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (account.AccountType == AccountType.CUSTOMER && account.CustomerId.HasValue)
            return new(account.CustomerId, null, null);
        if (account.AccountType == AccountType.EMPLOYEE && account.Employee is { } employee && Enum.IsDefined(employee.Role))
            return new(null, employee.EmployeeId, employee.Role);
        throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
    }

    private IQueryable<RoutePlanRoute> AssignedSegments(Actor actor) => db.RoutePlanRoutes.AsNoTracking().Where(segment =>
        VisiblePlanStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue &&
        (segment.SupersededAt == null || segment.ActualDepartureAt != null) &&
        (actor.Role == EmployeeRole.DRIVER && segment.DriverEmployeeId == actor.EmployeeId ||
         actor.Role == EmployeeRole.ESCORT && segment.EscortEmployeeId == actor.EmployeeId));

    private async Task<TransportData> GetVisibleTransportAsync(Actor actor, Guid transportId, CancellationToken cancellationToken)
    {
        if (transportId == Guid.Empty) throw new FieldValidationException([new("transportId", "Transport ID must be a non-empty UUID.")]);
        var transport = await TransportQuery().SingleOrDefaultAsync(item => item.TransportId == transportId &&
            (!actor.IsCustomer || item.CustomerId == actor.CustomerId), cancellationToken);
        if (transport is null) throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
        if (actor.IsCrew && !await AssignedSegments(actor).AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId, cancellationToken))
            throw new ForbiddenException("You are not assigned to this transport", "FORBIDDEN");
        if (!VisibleStatuses.Contains(transport.Status) || !transport.HasRoutePlan)
            throw new ConflictException("The transport has not started yet or has been cancelled", "INVALID_TRANSPORT_STATUS");
        return transport;
    }

    private IQueryable<TransportData> TransportQuery() => db.Transports.AsNoTracking().Select(item => new TransportData
    {
        TransportId = item.TransportId, TransportCode = item.TransportCode, Status = item.Status,
        BookingId = item.BookingId, BookingCode = item.Booking.BookingCode, CustomerId = item.Booking.CustomerId,
        CustomerName = item.Booking.Customer.FullName, OriginAddress = item.Booking.OriginAddress, OriginCountry = item.Booking.OriginCountry,
        DestinationAddress = item.Booking.DestinationAddress, DestinationCountry = item.Booking.DestinationCountry,
        ActualDepartureAt = item.ActualDepartureAt, ActualArrivalAt = item.ActualArrivalAt, CompletedAt = item.CompletedAt,
        DepartureAt = item.ActualDepartureAt ?? item.TransportPlans.SelectMany(plan => plan.RoutePlans).SelectMany(plan => plan.RoutePlanRoutes)
            .Where(segment => VisiblePlanStatuses.Contains(segment.RoutePlan.Status) && segment.RoutePlan.PrimaryRoutePlanId == null &&
                segment.RoutePlan.RoutePlanRoutes.All(other => other.RouteType == RouteType.PRIMARY))
            .OrderByDescending(segment => segment.RoutePlan.CreatedAt).ThenBy(segment => segment.SequenceNo)
            .Select(segment => segment.PlannedDepartureAt).FirstOrDefault(),
        HasRoutePlan = item.TransportPlans.Any(plan => plan.RoutePlans.Any(route => VisiblePlanStatuses.Contains(route.Status))),
        HorseCount = item.Booking.BookingHorses.Count,
        ActiveIncidentCount = db.IncidentReports.Count(incident => incident.TransportId == item.TransportId &&
            (incident.Status == IncidentStatus.REPORTED || incident.Status == IncidentStatus.PENDING))
    });

    private static TransportStatus[] ParseStatuses(Request.AssignedRequest request) =>
        (request.Status ?? "READY,IN_TRANSIT").Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<TransportStatus>).Distinct().ToArray();

    private sealed record Actor(Guid? CustomerId, Guid? EmployeeId, EmployeeRole? Role)
    {
        public bool IsCustomer => CustomerId.HasValue;
        public bool IsCrew => Role is EmployeeRole.DRIVER or EmployeeRole.ESCORT;
        public bool IsBackOffice => Role is EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.TRANSPORT_SPECIALIST;
    }

    private sealed class TransportData
    {
        public Guid TransportId { get; init; }
        public required string TransportCode { get; init; }
        public TransportStatus Status { get; init; }
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public Guid CustomerId { get; init; }
        public required string CustomerName { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public DateTime? ActualDepartureAt { get; init; }
        public DateTime? ActualArrivalAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public DateTime? DepartureAt { get; init; }
        public bool HasRoutePlan { get; init; }
        public int HorseCount { get; init; }
        public int ActiveIncidentCount { get; init; }
    }
}
