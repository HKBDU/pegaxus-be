using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.HandoverService;

public sealed partial class Service(AppDbContext db, TimeProvider clock, IValidator<Request.ListRequest> listValidator,
    IValidator<Request.CancelRequest> cancelValidator, IValidator<Request.CreateRequest> createValidator, INotificationService notifications,
    IValidator<Request.CompleteRequest> completeValidator, IMediaService media, ILogger<Service> logger) : IHandoverService
{
    private static readonly RoutePlanStatus[] EligibleStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];
    private static readonly EmployeeRole[] ViewerRoles = [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR,
        EmployeeRole.TRANSPORT_SPECIALIST, EmployeeRole.DRIVER, EmployeeRole.ESCORT];

    public async Task<Response.ListResponse> GetListAsync(Guid transportId, Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, listValidator);
        var actor = await ActorAsync(accountId, cancellationToken);
        var transport = await HeaderAsync(transportId, cancellationToken);
        await EnsureVisibleAsync(transport, actor, false, cancellationToken);
        if (transport.Status is not (TransportStatus.READY or TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED) ||
            !await db.RoutePlans.AsNoTracking().AnyAsync(plan => plan.TransportPlan.TransportId == transportId && EligibleStatuses.Contains(plan.Status), cancellationToken))
            throw new ConflictException("The transport has not started yet or has been cancelled", "INVALID_TRANSPORT_STATUS");
        var query = db.HandoverRecords.AsNoTracking().Where(item => item.TransportId == transportId);
        if (actor.IsCustomer) query = query.Where(item => item.HandoverType == HandoverType.PICKUP || item.HandoverType == HandoverType.DELIVERY);
        if (request.HandoverType is not null)
        {
            var types = request.HandoverType.Split(',').Select(value => Enum.Parse<HandoverType>(value.Trim(), true)).ToArray();
            query = query.Where(item => types.Contains(item.HandoverType));
        }
        if (request.Status is not null)
        {
            var statuses = request.Status.Split(',').Select(value => Enum.Parse<HandoverStatus>(value.Trim(), true)).ToArray();
            query = query.Where(item => statuses.Contains(item.Status));
        }
        var total = await query.CountAsync(cancellationToken);
        var sorted = request.SortBy == "sequenceNo:desc" ? query.OrderByDescending(item => item.SequenceNo) : query.OrderBy(item => item.SequenceNo);
        var items = await sorted.ThenBy(item => item.HandoverRecordId).Skip(request.PageIndex * request.PageSize).Take(request.PageSize)
            .Select(item => new Response.ListItem(item.HandoverRecordId, item.HandoverCode, item.SequenceNo, item.HandoverType.ToString(), item.Status.ToString(),
                item.FromPartyName, item.ToPartyName, item.LocationName, AsUtc(item.PlannedHandoverAt), AsUtc(item.ActualHandoverAt))).ToArrayAsync(cancellationToken);
        return new(clock.GetUtcNow(), items, request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.DetailResponse> GetAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        var item = await db.HandoverRecords.AsNoTracking().SingleOrDefaultAsync(item => item.HandoverRecordId == handoverRecordId, cancellationToken) ?? throw Missing();
        if (actor.IsCustomer && item.HandoverType is not (HandoverType.PICKUP or HandoverType.DELIVERY)) throw Missing();
        var transport = await HeaderAsync(item.TransportId, cancellationToken);
        await EnsureVisibleAsync(transport, actor, true, cancellationToken);
        return await DetailAsync(item, transport, actor, cancellationToken);
    }

    private async Task<Actor> ActorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var actor = await db.Accounts.AsNoTracking().Where(account => account.AccountId == accountId && !account.IsDeleted && account.Status == AccountStatus.ACTIVE)
            .Select(account => new Actor(account.AccountType, account.CustomerAccount != null && !account.CustomerAccount.IsDeleted,
                db.Employees.Active().Where(employee => employee.AccountId == account.AccountId).Select(employee => (Guid?)employee.EmployeeId).FirstOrDefault(),
                db.Employees.Active().Where(employee => employee.AccountId == account.AccountId).Select(employee => (EmployeeRole?)employee.Role).FirstOrDefault(), account.AccountId))
            .SingleOrDefaultAsync(cancellationToken);
        if (actor is null || actor.Type == AccountType.CUSTOMER && !actor.HasCustomer || actor.Type == AccountType.EMPLOYEE && actor.EmployeeId is null)
            throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (!actor.IsCustomer && (actor.Type != AccountType.EMPLOYEE || actor.Role is null || !ViewerRoles.Contains(actor.Role.Value))) throw Forbidden();
        return actor;
    }

    private async Task<Header> HeaderAsync(Guid transportId, CancellationToken cancellationToken) =>
        await db.Transports.AsNoTracking().Where(transport => transport.TransportId == transportId)
            .Select(transport => new Header(transport.TransportId, transport.TransportCode, transport.Status, transport.BookingId, transport.Booking.Customer.AccountId))
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");

    private async Task EnsureVisibleAsync(Header transport, Actor actor, bool record, CancellationToken cancellationToken)
    {
        if (actor.IsCustomer && actor.AccountId != transport.CustomerAccountId)
        {
            if (record) throw Missing();
            throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
        }
        if (actor.IsCrew && !await AssignedSegments(transport.Id, actor.EmployeeId).AnyAsync(cancellationToken))
            throw new ForbiddenException("You are not assigned to this transport", "FORBIDDEN");
    }

    private IQueryable<RoutePlanRoute> AssignedSegments(Guid transportId, Guid? employeeId) =>
        db.RoutePlanRoutes.AsNoTracking().Where(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
            EligibleStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue &&
            (segment.SupersededAt == null || segment.ActualDepartureAt != null) && (segment.DriverEmployeeId == employeeId || segment.EscortEmployeeId == employeeId));

    private Task<bool> CanOperateAsync(HandoverRecord item, Actor actor, CancellationToken cancellationToken) => actor.IsManager
        ? Task.FromResult(true)
        : actor.IsCrew ? AssignedSegments(item.TransportId, actor.EmployeeId)
            .AnyAsync(segment => segment.RoutePlanRouteId == item.FromRoutePlanRouteId || segment.RoutePlanRouteId == item.ToRoutePlanRouteId, cancellationToken)
            : Task.FromResult(false);

    private static NotFoundException Missing() => new("Handover record not found", "HANDOVER_RECORD_NOT_FOUND");
    private static ForbiddenException Forbidden() => new("You are not allowed to perform this action on this handover record", "FORBIDDEN");
    private sealed record Header(Guid Id, string Code, TransportStatus Status, Guid BookingId, Guid CustomerAccountId);
    private sealed record Actor(AccountType Type, bool HasCustomer, Guid? EmployeeId, EmployeeRole? Role, Guid AccountId)
    {
        internal bool IsCustomer => Type == AccountType.CUSTOMER;
        internal bool IsCrew => Type == AccountType.EMPLOYEE && Role is EmployeeRole.DRIVER or EmployeeRole.ESCORT;
        internal bool IsManager => Type == AccountType.EMPLOYEE && Role is EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR;
    }
}
