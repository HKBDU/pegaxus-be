using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.HorseHealthService;

public sealed partial class Service(AppDbContext db, TimeProvider clock, IValidator<Request.ListRequest> listValidator,
    IMediaService media, IDocumentStorageService documentStorage) : IHorseHealthService
{
    private static readonly RoutePlanStatus[] VisiblePlanStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];

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

    private async Task<TransportData> GetVisibleTransportAsync(Guid transportId, Actor actor, CancellationToken cancellationToken)
    {
        if (transportId == Guid.Empty)
            throw new FieldValidationException([new("transportId", "Transport ID must be a non-empty UUID.")]);
        var transport = await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId &&
                (!actor.IsCustomer || item.Booking.CustomerId == actor.CustomerId))
            .Select(item => new TransportData(item.BookingId, item.Status,
                item.TransportPlans.Any(plan => plan.RoutePlans.Any(route => VisiblePlanStatuses.Contains(route.Status)))))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
        if (actor.Role is EmployeeRole.DRIVER or EmployeeRole.ESCORT &&
            !await db.RoutePlanRoutes.AsNoTracking().AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
                VisiblePlanStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue &&
                (segment.SupersededAt == null || segment.ActualDepartureAt != null) &&
                (actor.Role == EmployeeRole.DRIVER && segment.DriverEmployeeId == actor.EmployeeId ||
                 actor.Role == EmployeeRole.ESCORT && segment.EscortEmployeeId == actor.EmployeeId), cancellationToken))
            throw new ForbiddenException("You are not assigned to this transport", "FORBIDDEN");
        if (transport.Status is not (TransportStatus.READY or TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED) || !transport.HasRoutePlan)
            throw new ConflictException("The transport has not started yet or has been cancelled", "INVALID_TRANSPORT_STATUS");
        return transport;
    }

    private sealed record Actor(Guid? CustomerId, Guid? EmployeeId, EmployeeRole? Role)
    {
        public bool IsCustomer => CustomerId.HasValue;
    }

    private sealed record TransportData(Guid BookingId, TransportStatus Status, bool HasRoutePlan);
}
