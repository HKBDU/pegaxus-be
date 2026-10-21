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
    IValidator<Request.ReportHorseHealthRequest> horseReportValidator, IValidator<Request.AddHorseDetailsRequest> detailsValidator,
    IValidator<Request.FollowUpHorseRequest> followUpValidator, IValidator<Request.ListRequest> listValidator,
    IValidator<Request.ResolveRequest> resolveValidator, IValidator<Request.CancelRequest> cancelValidator,
    IValidator<Request.SelfResolveRequest> selfResolveValidator, IValidator<Request.RaiseSeverityRequest> severityValidator,
    IValidator<Request.RerouteEscalationRequest> escalationValidator, IValidator<Request.RouteHoldRequest> holdValidator,
    IMediaService media, IDocumentStorageService documentStorage, INotificationService notifications) : IIncidentService
{
    private static readonly RoutePlanStatus[] AssignmentStatuses = [RoutePlanStatus.CONFIRMED, RoutePlanStatus.ACTIVE, RoutePlanStatus.COMPLETED];
    private const string ForbiddenMessage = "Only the assigned driver / escort can report incidents for this transport";

    private async Task<Employee> ActorAsync(Guid accountId, CancellationToken ct)
    {
        var account = await AccountAsync(accountId, ct);
        if (account.AccountType != AccountType.EMPLOYEE) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        var employee = await db.Employees.AsNoTracking().Active().SingleOrDefaultAsync(item => item.AccountId == accountId, ct)
            ?? throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");
        if (employee.Role is not (EmployeeRole.LOGISTICS_MANAGER or EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.TRANSPORT_SPECIALIST or EmployeeRole.DRIVER or EmployeeRole.ESCORT))
            throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        return employee;
    }

    private async Task<Account> AccountAsync(Guid accountId, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.AccountId == accountId &&
            !item.IsDeleted && item.Status == AccountStatus.ACTIVE, ct)
        ?? throw new UnauthorizedException("The account is inactive or unavailable", "UNAUTHORIZED");

    private async Task<Transport> TransportAsync(Guid transportId, CancellationToken ct)
    {
        if (transportId == Guid.Empty) throw new BadRequestException("Transport ID must be a non-empty UUID.", "VALIDATION_ERROR");
        return await db.Transports.AsNoTracking().Include(item => item.Booking).ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.TransportId == transportId, ct)
            ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");
    }

    private Task<bool> CrewAssignedAsync(Guid transportId, Employee actor, CancellationToken ct) =>
        CrewSegments(actor).AnyAsync(segment => segment.RoutePlan.TransportPlan.TransportId == transportId, ct);

    private IQueryable<RoutePlanRoute> CrewSegments(Employee actor) =>
        db.RoutePlanRoutes.AsNoTracking().Where(segment =>
            AssignmentStatuses.Contains(segment.RoutePlan.Status) && !segment.TransportProviderId.HasValue &&
            (segment.SupersededAt == null || segment.ActualDepartureAt != null) &&
            (actor.Role == EmployeeRole.DRIVER && segment.DriverEmployeeId == actor.EmployeeId ||
             actor.Role == EmployeeRole.ESCORT && segment.EscortEmployeeId == actor.EmployeeId));

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

    private async Task<(IncidentReport Incident, Transport Transport)> LockIncidentAsync(Guid incidentReportId, CancellationToken ct)
    {
        if (incidentReportId == Guid.Empty) throw new BadRequestException("Incident ID must be a non-empty UUID.", "VALIDATION_ERROR");
        var transportId = await db.IncidentReports.AsNoTracking().Where(item => item.IncidentReportId == incidentReportId)
            .Select(item => item.TransportId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND");
        // All incident writers lock Transport before Incident, then details in ID order.
        await db.LockRowsAsync(db.Transports.Where(item => item.TransportId == transportId), item => item.TransportId, ct);
        await db.LockRowsAsync(db.IncidentReports.Where(item => item.IncidentReportId == incidentReportId), item => item.IncidentReportId, ct);
        var incident = await db.IncidentReports.SingleAsync(item => item.IncidentReportId == incidentReportId, ct);
        await db.Entry(incident).ReloadAsync(ct);
        if (incident.TransportId != transportId)
            throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
        return (incident, await TransportAsync(transportId, ct));
    }

    private static void EnsureOpenHorseIncident(IncidentReport incident)
    {
        if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
            throw new UnprocessableEntityException("The incident is already resolved or cancelled", "INVALID_INCIDENT_STATUS");
        if (incident.Category is not (IncidentCategory.HORSE_HEALTH or IncidentCategory.BOTH))
            throw new UnprocessableEntityException("This incident does not accept this kind of detail", "INCIDENT_CATEGORY_MISMATCH");
    }

    private DateTime NextUpdate(IncidentReport incident)
    {
        var now = DbTime.Truncate(clock.GetUtcNow());
        return now > incident.UpdatedAt ? now : incident.UpdatedAt.AddTicks(10);
    }
}
