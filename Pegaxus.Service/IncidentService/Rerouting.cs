using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    private const string RerouteForbiddenMessage = "Only the assigned coordinator / logistics manager can access emergency rerouting";

    public async Task<Response.RerouteContext> GetRerouteContextAsync(Guid incidentReportId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        var incident = await db.IncidentReports.AsNoTracking().Include(item => item.VehicleIncidentDetail)
            .SingleOrDefaultAsync(item => item.IncidentReportId == incidentReportId, ct)
            ?? throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND");
        var transport = await TransportAsync(incident.TransportId ?? throw new NotFoundException("Incident not found", "INCIDENT_NOT_FOUND"), ct);
        var actor = await RerouteActorAsync(transport, accountId, null, ct);
        var planId = await CurrentPlanIdAsync(transport.TransportId, ct);
        var plans = await db.RoutePlans.AsNoTracking().AsSplitQuery()
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(segment => segment.Route).ThenInclude(route => route.FromLocation)
            .Include(plan => plan.RoutePlanRoutes).ThenInclude(segment => segment.Route).ThenInclude(route => route.ToLocation)
            .Where(plan => plan.TransportPlanId == planId && plan.Status != RoutePlanStatus.CANCELLED)
            .OrderByDescending(plan => plan.CreatedAt).ThenBy(plan => plan.RoutePlanId).ToArrayAsync(ct);
        var itinerary = TransportItinerary.Effective(plans);
        var current = TransportItinerary.Current(itinerary, transport.Status);
        var primary = plans.Where(TransportItinerary.IsPrimaryPlan).Where(plan => AssignmentStatuses.Contains(plan.Status))
            .OrderBy(plan => plan.Status == RoutePlanStatus.ACTIVE ? 0 : plan.Status == RoutePlanStatus.COMPLETED ? 1 : 2).FirstOrDefault();
        var primaryArrival = primary?.RoutePlanRoutes.OrderBy(segment => segment.SequenceNo).LastOrDefault()?.PlannedArrivalAt;
        var remaining = itinerary.Where(segment => !segment.Route.ActualDepartureAt.HasValue && !segment.Route.ActualArrivalAt.HasValue).ToArray();
        var backups = plans.Where(TransportItinerary.IsBackupPlan)
            .Select(plan => RerouteOption(plan, plans, primary, primaryArrival, remaining.Length > 0)).ToArray();
        var returning = plans.FirstOrDefault(plan => TransportItinerary.IsReturnPlan(plan) && plan.Status != RoutePlanStatus.REJECTED_BY_MANAGER);
        var returnOption = returning is null ? null : RerouteOption(returning, plans, primary, primaryArrival, remaining.Length > 0);
        var dossiers = await db.ComplianceDossiers.AsNoTracking().Where(dossier => dossier.TransportId == transport.TransportId)
            .OrderBy(dossier => dossier.DossierType).Select(dossier => new Response.RerouteDossier(dossier.ComplianceDossierId,
                dossier.DossierCode, dossier.DossierType.ToString(), dossier.Status.ToString())).ToArrayAsync(ct);
        var activeHold = await ActiveTransportHoldAsync(transport.TransportId, ct);
        var hold = await ReadHoldAsync(incidentReportId, ct);
        var actions = new List<string>();
        if (incident.Status is IncidentStatus.REPORTED or IncidentStatus.PENDING && transport.Status == TransportStatus.IN_TRANSIT)
        {
            if (actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            {
                if (backups.Any(plan => plan.IsAvailable)) actions.Add("REQUEST_BACKUP");
                actions.Add("ESCALATE_REROUTE");
            }
            else
            {
                if (backups.Any(plan => plan.IsAvailable)) actions.Add("APPROVE_BACKUP");
                if (activeHold is null) actions.Add("HOLD_ROUTE");
                if (returnOption is { IsAvailable: true } && dossiers.Any(dossier => dossier.DossierType == "MAIN" && dossier.Status == "APPROVED") &&
                    dossiers.Any(dossier => dossier.DossierType == "EXTRA" && dossier.Status == "APPROVED")) actions.Add("ACTIVATE_RETURN");
            }
            if (returning is null) actions.Add("CREATE_RETURN");
        }
        return new(clock.GetUtcNow(), new(incident.IncidentReportId, incident.IncidentCode, incident.Category.ToString(), incident.Severity.ToString(),
                incident.Status.ToString(), incident.Title, incident.VehicleIncidentDetail?.VehicleOperable, AsUtc(incident.OccurredAt)),
            new(transport.TransportId, transport.TransportCode, transport.Status.ToString(), planId),
            current is { Status: RouteSegmentStatus.IN_TRANSIT } ? RerouteSegment(current.Route) : null,
            remaining.Select(segment => RerouteSegment(segment.Route)).ToArray(), backups, returnOption, dossiers, hold, actions);
    }

    private static Response.ReroutePlan RerouteOption(RoutePlan plan, RoutePlan[] plans, RoutePlan? primary, DateTime? primaryArrival, bool hasRemaining)
    {
        var segments = plan.RoutePlanRoutes.OrderBy(segment => segment.SequenceNo).ToArray();
        var checks = RouteRestRules.Evaluate(segments.Select(segment => new RouteRestRules.Leg(segment.PlannedDepartureAt,
            segment.PlannedArrivalAt, segment.Route.TransportMode, segment.Route.FromLocation.LocationType, segment.Route.ToLocation.LocationType)).ToArray());
        var backup = TransportItinerary.IsBackupPlan(plan);
        var branch = backup ? TransportItinerary.BranchSegment(plan) : null;
        var tail = segments.Where(segment => !backup || segment.SequenceNo >= branch!.SequenceNo).ToArray();
        var referencedPrimary = plan.PrimaryRoutePlanId.HasValue ? plans.FirstOrDefault(item => item.RoutePlanId == plan.PrimaryRoutePlanId) : primary;
        var replaceable = backup ? referencedPrimary?.RoutePlanRoutes.Any(segment => segment.SupersededAt is null &&
            !segment.ActualDepartureAt.HasValue && !segment.ActualArrivalAt.HasValue) == true : hasRemaining;
        var reason = plan.Status == RoutePlanStatus.ACTIVE ? "ALREADY_ACTIVE"
            : plan.Status != RoutePlanStatus.CONFIRMED ? "NOT_CONFIRMED"
            : !replaceable || tail.Length == 0 || tail.Any(segment => segment.SupersededAt.HasValue || segment.ActualDepartureAt.HasValue || segment.ActualArrivalAt.HasValue) ? "SEGMENT_LOCKED"
            : checks.Any(check => !check.Passed) ? "REST_RULE_VIOLATION" : null;
        var arrival = segments.LastOrDefault()?.PlannedArrivalAt;
        return new(plan.RoutePlanId, plan.RoutePlanCode, plan.Status.ToString(), plan.TotalDistanceKm, plan.EstimatedDurationMinutes,
            AsUtc(arrival), arrival.HasValue && primaryArrival.HasValue ? (decimal)(arrival.Value - primaryArrival.Value).TotalMinutes : null,
            reason is null, reason, checks, branch?.SequenceNo, LocationBrief.From(branch?.Route.FromLocation));
    }

    private static Response.RerouteSegment RerouteSegment(RoutePlanRoute segment) =>
        new(segment.RoutePlanRouteId, segment.RoutePlanId, segment.SequenceNo, segment.RouteType.ToString(),
            LocationBrief.From(segment.Route.FromLocation), LocationBrief.From(segment.Route.ToLocation),
            TransportItinerary.StatusOf(segment).ToString(), AsUtc(segment.PlannedArrivalAt), AsUtc(segment.ActualDepartureAt), AsUtc(segment.ActualArrivalAt));

    private async Task<Employee> RerouteActorAsync(Transport transport, Guid accountId, EmployeeRole? requiredRole, CancellationToken ct)
    {
        var actor = await ActorAsync(accountId, ct);
        if (requiredRole.HasValue && actor.Role != requiredRole || actor.Role != EmployeeRole.LOGISTICS_MANAGER &&
            !await CoordinatorAssignedAsync(transport.TransportId, actor, ct))
            throw new ForbiddenException(RerouteForbiddenMessage, "FORBIDDEN");
        return actor;
    }

    private static void EnsureRerouteWritable(IncidentReport incident, Transport transport)
    {
        if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
            throw new UnprocessableEntityException("The incident is already resolved or cancelled", "INVALID_INCIDENT_STATUS");
        if (transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
    }

    private static Response.Reporter RerouteEmployee(Employee employee) =>
        new(employee.EmployeeId, employee.EmployeeCode, employee.FullName, employee.Role.ToString());
}
