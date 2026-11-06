using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    public async Task<Response.CostListResult> ListCostsAsync(Request.CostListRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        Validation.ValidateFields(request, costListValidator);
        var actor = await ActorAsync(accountId, ct);
        var query = VisibleCosts(actor);
        var groups = await query.GroupBy(cost => cost.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToArrayAsync(ct);
        int Count(EmergencyCostStatus status) => groups.Where(group => group.Status == status).Sum(group => group.Count);
        var counts = new Response.CostCounts(Count(EmergencyCostStatus.PENDING), Count(EmergencyCostStatus.APPROVED),
            Count(EmergencyCostStatus.REJECTED), Count(EmergencyCostStatus.CANCELLED));
        var status = request.Status ?? (actor.Role == EmployeeRole.LOGISTICS_MANAGER ? "PENDING" : null);
        if (status is not null)
        {
            var statuses = status.Split(',', StringSplitOptions.TrimEntries).Select(Enum.Parse<EmergencyCostStatus>).ToArray();
            query = query.Where(cost => statuses.Contains(cost.Status));
        }
        if (request.Category is { } category)
        {
            var parsed = Enum.Parse<EmergencyCostCategory>(category); query = query.Where(cost => cost.Category == parsed);
        }
        if (request.IncidentReportId is { } incidentId) query = query.Where(cost => cost.IncidentReportId == incidentId);
        if (request.TransportId is { } transportId) query = query.Where(cost => cost.IncidentReport.TransportId == transportId);
        if (request.FromDate is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue); query = query.Where(cost => cost.RequestedAt >= start);
        }
        if (request.ToDate is { } to)
        {
            var end = to.ToDateTime(TimeOnly.MaxValue); query = query.Where(cost => cost.RequestedAt <= end);
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLowerInvariant();
            query = query.Where(cost => cost.RequestCode.ToLower().Contains(keyword) || cost.IncidentReport.IncidentCode.ToLower().Contains(keyword));
        }
        var total = await query.CountAsync(ct);
        var page = await CostGraph(SortCosts(query, request.SortBy).Skip(Paging.Offset(request.PageIndex, request.PageSize)).Take(request.PageSize)).ToArrayAsync(ct);
        var ids = page.Select(cost => cost.IncidentReport.TransportId).ToArray();
        var transports = await db.Transports.AsNoTracking().Where(transport => ids.Contains(transport.TransportId))
            .ToDictionaryAsync(transport => transport.TransportId, transport => transport.TransportCode, ct);
        var items = page.Select(cost => MapCost(cost) with { Incident = MapCostIncident(cost.IncidentReport),
            TransportCode = transports[cost.IncidentReport.TransportId!.Value] }).ToArray();
        return new(clock.GetUtcNow(), counts, items, request.PageIndex, request.PageSize, total, Paging.TotalPages(total, request.PageSize));
    }

    public async Task<Response.CostRequest> GetCostAsync(Guid emergencyCostRequestId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        if (emergencyCostRequestId == Guid.Empty) throw new BadRequestException("Emergency cost request ID must be a non-empty UUID.", "VALIDATION_ERROR");
        var actor = await ActorAsync(accountId, ct);
        if (!await VisibleCosts(actor).AnyAsync(cost => cost.EmergencyCostRequestId == emergencyCostRequestId, ct))
            throw new NotFoundException("Emergency cost request not found", "EMERGENCY_COST_NOT_FOUND");
        var response = await ReadCostAsync(emergencyCostRequestId, ct);
        var incident = await db.IncidentReports.AsNoTracking().Include(item => item.Location)
            .Include(item => item.VehicleIncidentDetail).ThenInclude(detail => detail!.Vehicle)
            .SingleAsync(item => item.IncidentReportId == response.Incident!.IncidentReportId, ct);
        var transport = await TransportAsync(incident.TransportId!.Value, ct);
        // A proposer sees only their own costs, including the related-request panel.
        var otherRequests = await VisibleCosts(actor).Where(cost => cost.IncidentReportId == incident.IncidentReportId &&
                cost.EmergencyCostRequestId != emergencyCostRequestId).OrderBy(cost => cost.RequestedAt).ThenBy(cost => cost.EmergencyCostRequestId)
            .Select(cost => new Response.OtherCostRequest(cost.EmergencyCostRequestId, cost.RequestCode, cost.Category.ToString(),
                Vnd.ToLong(cost.RequestedAmount), Whole(cost.ApprovedAmount), cost.Status.ToString())).ToArrayAsync(ct);
        var health = await db.BookingHorses.AsNoTracking().Where(item => item.BookingId == transport.BookingId)
            .Select(item => db.HorseHealthLogs.Where(log => log.TransportId == transport.TransportId && log.HorseId == item.HorseId)
                .OrderByDescending(log => log.CheckedAt).ThenByDescending(log => log.CreatedAt).ThenByDescending(log => log.HorseHealthLogId)
                .Select(log => (HorseHealthStatus?)log.HealthStatus).FirstOrDefault()).ToArrayAsync(ct);
        var segment = await CurrentSegmentAsync(transport, ct);
        var currentRoute = segment is null ? null : await db.RoutePlans.AsNoTracking().Where(plan => plan.RoutePlanId == segment.RoutePlanId)
            .Select(plan => new Response.CostRoute(plan.RoutePlanId, plan.RoutePlanCode, segment.RouteType.ToString(), plan.Status.ToString())).SingleAsync(ct);
        var open = incident.Status is IncidentStatus.REPORTED or IncidentStatus.PENDING;
        var pending = response.Status == nameof(EmergencyCostStatus.PENDING) && response.Decision is null;
        var actions = new List<string>();
        if (pending && open && actor.Role == EmployeeRole.LOGISTICS_MANAGER) actions.Add("DECIDE");
        if (pending && response.RequestedBy.EmployeeId == actor.EmployeeId && (Crew(actor) || actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR)) actions.Add("CANCEL");
        return response with
        {
            AvailableActions = actions, IncidentSummary = new(incident.IncidentReportId, incident.IncidentCode, incident.Category.ToString(),
                incident.Severity.ToString(), incident.Status.ToString(), incident.Title, AsUtc(incident.OccurredAt), LocationBrief.From(incident.Location),
                incident.VehicleIncidentDetail is { } detail ? VehicleBrief(detail.Vehicle) : null), OtherRequests = otherRequests,
            HorseHealthSummary = new(health.Length, health.Count(item => item == HorseHealthStatus.NORMAL), health.Count(item => item == HorseHealthStatus.OBSERVATION_REQUIRED),
                health.Count(item => item == HorseHealthStatus.AT_RISK), health.Count(item => item == HorseHealthStatus.CRITICAL), health.Count(item => item == null)),
            CurrentRoute = currentRoute, ServerTime = clock.GetUtcNow()
        };
    }

    private IQueryable<EmergencyCostRequest> VisibleCosts(Employee actor)
    {
        var query = db.EmergencyCostRequests.AsNoTracking().Where(cost => cost.IncidentReport.TransportId.HasValue &&
            db.Transports.Any(transport => transport.TransportId == cost.IncidentReport.TransportId));
        if (Crew(actor)) return query.Where(cost => cost.RequestedByEmployeeId == actor.EmployeeId);
        if (actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR)
            return query.Where(cost => db.EmployeeTransportPlans.Any(assignment => assignment.EmployeeId == actor.EmployeeId &&
                assignment.TransportPlanId == db.TransportPlans.Where(plan => plan.TransportId == cost.IncidentReport.TransportId && plan.Status != TransportPlanStatus.CANCELLED)
                    .OrderByDescending(plan => plan.CreatedAt).ThenBy(plan => plan.TransportPlanId).Select(plan => plan.TransportPlanId).FirstOrDefault()));
        return query;
    }

    private static IQueryable<EmergencyCostRequest> CostGraph(IQueryable<EmergencyCostRequest> query) => query.AsSplitQuery()
        .Include(cost => cost.IncidentReport).Include(cost => cost.RequestedByEmployee)
        .Include(cost => cost.CostApprovalDecision).ThenInclude(decision => decision!.Employee);

    private async Task<Response.CostRequest> ReadCostAsync(Guid costId, CancellationToken ct)
    {
        var cost = await CostGraph(db.EmergencyCostRequests.AsNoTracking()).Include(item => item.IncidentPhotos)
            .SingleAsync(item => item.EmergencyCostRequestId == costId, ct);
        var photos = new List<Response.Photo>();
        foreach (var photo in cost.IncidentPhotos.OrderBy(item => item.SortOrder).ThenBy(item => item.IncidentPhotoId))
        {
            var signed = photo.PhotoUrl.StartsWith("s3://", StringComparison.Ordinal)
                ? await documentStorage.CreateReadUrlAsync(photo.PhotoUrl, ct) : await media.CreateReadUrlAsync(photo.PhotoUrl, ct);
            photos.Add(new(photo.IncidentPhotoId, signed.Url, photo.Caption, null, photo.SortOrder));
        }
        return MapCost(cost) with { Incident = MapCostIncident(cost.IncidentReport), Photos = photos };
    }

    private static Response.CostIncident MapCostIncident(IncidentReport incident) => new(incident.IncidentReportId, incident.IncidentCode,
        incident.Category.ToString(), incident.Severity.ToString(), incident.Status.ToString(), incident.TransportId);

    private static Response.CostRequest MapCost(EmergencyCostRequest cost) => new(cost.EmergencyCostRequestId, cost.RequestCode, cost.Category.ToString(),
        cost.Description, cost.Justification, Vnd.ToLong(cost.RequestedAmount), Whole(cost.ApprovedAmount), Vnd.Currency, cost.Status.ToString(), AsUtc(cost.RequestedAt),
        EmployeeBrief(cost.RequestedByEmployee), cost.CostApprovalDecision is { } decision ? new(decision.Decision.ToString(), Whole(decision.ApprovedAmount),
            decision.DecisionReason, EmployeeBrief(decision.Employee), AsUtc(decision.DecidedAt)) : null, AsUtc(cost.CancelledAt), cost.CancellationReason,
        cost.InvoiceRequired, AsUtc(cost.CreatedAt), AsUtc(cost.UpdatedAt));

    private static IOrderedQueryable<EmergencyCostRequest> SortCosts(IQueryable<EmergencyCostRequest> query, string sortBy)
    {
        IOrderedQueryable<EmergencyCostRequest>? ordered = null;
        foreach (var token in sortBy.Split(',', StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':');
            Expression<Func<EmergencyCostRequest, object>> key = parts[0] switch
            {
                "updatedAt" => cost => cost.UpdatedAt, "requestedAmount" => cost => cost.RequestedAmount,
                "status" => cost => cost.Status, _ => cost => cost.RequestedAt
            };
            ordered = ordered is null ? parts[1] == "desc" ? query.OrderByDescending(key) : query.OrderBy(key)
                : parts[1] == "desc" ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
        }
        return (ordered ?? query.OrderByDescending(cost => cost.RequestedAt)).ThenBy(cost => cost.EmergencyCostRequestId);
    }
}
