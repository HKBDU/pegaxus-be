using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.LocationTrackingService;

public sealed partial class Service(AppDbContext db, TimeProvider clock, TrackingState state, IOptions<TrackingOptions> options,
    IValidator<Request.LocationRequest> validator, ILocationTrackingRealtimeService realtime, INotificationService notifications,
    ILogger<Service> logger) : ILocationTrackingService
{
    private static readonly RoutePlanStatus[] EligibleStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];
    private static readonly EmployeeRole[] ViewerRoles = [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR, EmployeeRole.TRANSPORT_SPECIALIST];
    private const string ForbiddenMessage = "You are not assigned to this transport";

    public async Task<Response.Snapshot> GetAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var transport = await ViewerTransportAsync(transportId, accountId, cancellationToken);
        var segments = await EffectiveSegmentsAsync(transportId, transport.Status, cancellationToken);
        var current = CurrentSegment(segments, transport.Status);
        var point = state.For(transportId).Latest;
        return new(clock.GetUtcNow(), transportId, transport.Status.ToString(),
            new(segments.Select((segment, index) => new Response.Segment(segment.Id, index + 1, SegmentStatus(segment), segment.Mode,
                segment.From, segment.To)).ToArray()), current?.To,
            segments.LastOrDefault()?.PlannedArrivalAt is { } eta ? AsUtc(eta) : null,
            segments.Where(segment => segment.ActualArrivalAt is null).Sum(segment => segment.DistanceKm ?? 0),
            point is null ? null : new(point.Latitude, point.Longitude, point.HeadingDegrees, point.SpeedKmh, point.AccuracyMeters, point.RecordedAt, point.RoutePlanRouteId));
    }

    public async Task EnsureViewerAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default) =>
        _ = await ViewerTransportAsync(transportId, accountId, cancellationToken);

    private async Task<Transport> ViewerTransportAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        if (actor.Type != AccountType.CUSTOMER && actor.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.TRANSPORT_SPECIALIST))
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        var transport = await TransportAsync(transportId, cancellationToken);
        if (actor.Type == AccountType.CUSTOMER && !await db.Bookings.AsNoTracking().AnyAsync(booking => booking.BookingId == transport.BookingId &&
                booking.Customer.AccountId == accountId, cancellationToken))
            throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
        if (transport.Status is not (TransportStatus.READY or TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED) ||
            !await db.RoutePlans.AsNoTracking().AnyAsync(plan => plan.TransportPlan.TransportId == transportId && EligibleStatuses.Contains(plan.Status), cancellationToken))
            throw new ConflictException("The transport has not started yet or has been cancelled", "INVALID_TRANSPORT_STATUS");
        return transport;
    }

    private async Task<(Actor Actor, Transport Transport)> DriverTransportAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(accountId, cancellationToken);
        if (actor.Type != AccountType.EMPLOYEE || actor.Role != EmployeeRole.DRIVER)
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        var transport = await TransportAsync(transportId, cancellationToken);
        if (!await db.RoutePlanRoutes.AsNoTracking().AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
                EligibleStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue && segment.DriverEmployeeId == actor.EmployeeId &&
                (segment.SupersededAt == null || segment.ActualDepartureAt != null), cancellationToken))
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        if (transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("Locations can only be recorded while the transport is IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
        return (actor, transport);
    }

    private async Task<Actor> ActorAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var actor = await db.Accounts.AsNoTracking().Where(account => account.AccountId == accountId && !account.IsDeleted && account.Status == AccountStatus.ACTIVE)
            .Select(account => new Actor(account.AccountType,
                db.Employees.Active().Where(employee => employee.AccountId == account.AccountId).Select(employee => (Guid?)employee.EmployeeId).FirstOrDefault(),
                db.Employees.Active().Where(employee => employee.AccountId == account.AccountId).Select(employee => (EmployeeRole?)employee.Role).FirstOrDefault(),
                db.Employees.Active().Where(employee => employee.AccountId == account.AccountId).Select(employee => employee.FullName).FirstOrDefault(),
                account.CustomerAccount != null && !account.CustomerAccount.IsDeleted)).SingleOrDefaultAsync(cancellationToken);
        if (actor is null || actor.Type == AccountType.EMPLOYEE && actor.EmployeeId is null || actor.Type == AccountType.CUSTOMER && !actor.HasCustomer)
            throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        return actor;
    }

    private async Task<Transport> TransportAsync(Guid transportId, CancellationToken cancellationToken)
    {
        if (transportId == Guid.Empty) throw new BadRequestException("Transport ID must be a non-empty UUID.", "VALIDATION_ERROR");
        return await db.Transports.AsNoTracking().SingleOrDefaultAsync(transport => transport.TransportId == transportId, cancellationToken)
            ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
    }

    private Task<Guid[]> ViewerAccountsAsync(Guid transportId, CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking().Where(account => !account.IsDeleted && account.Status == AccountStatus.ACTIVE &&
            (account.AccountType == AccountType.EMPLOYEE && db.Employees.Active().Any(employee => employee.AccountId == account.AccountId && ViewerRoles.Contains(employee.Role)) ||
             account.AccountType == AccountType.CUSTOMER && account.CustomerAccount != null && !account.CustomerAccount.IsDeleted &&
                db.Transports.Any(transport => transport.TransportId == transportId && transport.Booking.Customer.AccountId == account.AccountId)))
            .Select(account => account.AccountId).ToArrayAsync(cancellationToken);

    private sealed record Actor(AccountType Type, Guid? EmployeeId, EmployeeRole? Role, string? FullName, bool HasCustomer);
}
