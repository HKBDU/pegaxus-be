using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service(AppDbContext db, TimeProvider clock,
    IValidator<Request.ReportVehicleRequest> reportValidator, IValidator<Request.UpdateVehicleRequest> updateValidator,
    IMediaService media, IDocumentStorageService documentStorage, INotificationService notifications) : IIncidentService
{
    private static readonly RoutePlanStatus[] AssignmentStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];
    private const string ForbiddenMessage = "Only the assigned driver / escort can report incidents for this transport";

    private async Task<Employee> ActorAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.AccountId == accountId &&
            !item.IsDeleted && item.Status == AccountStatus.ACTIVE, ct)
            ?? throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (account.AccountType != AccountType.EMPLOYEE) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        var employee = await db.Employees.AsNoTracking().Active().SingleOrDefaultAsync(item => item.AccountId == accountId, ct)
            ?? throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.DRIVER or EmployeeRole.ESCORT))
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        return employee;
    }

    private async Task<Transport> TransportAsync(Guid transportId, CancellationToken ct)
    {
        if (transportId == Guid.Empty) throw new BadRequestException("Transport ID must be a non-empty UUID.", "VALIDATION_ERROR");
        return await db.Transports.AsNoTracking().Include(item => item.Booking).ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.TransportId == transportId, ct)
            ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
    }

    private Task<bool> CrewAssignedAsync(Guid transportId, Employee actor, CancellationToken ct) =>
        db.RoutePlanRoutes.AsNoTracking().AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId &&
            AssignmentStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue &&
            (segment.SupersededAt == null || segment.ActualDepartureAt != null) &&
            (actor.Role == EmployeeRole.DRIVER && segment.DriverEmployeeId == actor.EmployeeId ||
             actor.Role == EmployeeRole.ESCORT && segment.EscortEmployeeId == actor.EmployeeId), ct);

    private Task<Guid?> CurrentPlanIdAsync(Guid transportId, CancellationToken ct) =>
        db.TransportPlans.AsNoTracking().Where(plan => plan.TransportId == transportId && plan.Status != TransportPlanStatus.CANCELLED)
            .OrderByDescending(plan => plan.CreatedAt).ThenBy(plan => plan.TransportPlanId)
            .Select(plan => (Guid?)plan.TransportPlanId).FirstOrDefaultAsync(ct);

    private async Task<RoutePlanRoute?> CurrentSegmentAsync(Transport transport, CancellationToken ct)
    {
        var plans = await db.RoutePlans.AsNoTracking().Include(plan => plan.RoutePlanRoutes)
            .Where(plan => plan.TransportPlan.TransportId == transport.TransportId && AssignmentStatuses.Contains(plan.Status)).ToArrayAsync(ct);
        return TransportItinerary.Current(TransportItinerary.Effective(plans), transport.Status)?.Route;
    }

    private static bool Crew(Employee actor) => actor.Role is EmployeeRole.DRIVER or EmployeeRole.ESCORT;

    private async Task<bool> CoordinatorAssignedAsync(Guid transportId, Employee actor, CancellationToken ct)
    {
        var planId = await CurrentPlanIdAsync(transportId, ct);
        return actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR && await db.EmployeeTransportPlans.AsNoTracking()
            .AnyAsync(item => item.TransportPlanId == planId && item.EmployeeId == actor.EmployeeId, ct);
    }
}
