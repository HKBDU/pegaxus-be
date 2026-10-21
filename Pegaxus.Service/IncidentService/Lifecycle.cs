using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.IncidentService;

public sealed partial class Service
{
    public async Task<Response.Incident> ResolveAsync(Guid incidentReportId, Request.ResolveRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, resolveValidator);
        if (!request.EmergencySettlementConfirmed)
            throw new BadRequestException("Emergency settlement must be confirmed before closing the incident", "SETTLEMENT_NOT_CONFIRMED");
        return (await ChangeLifecycleAsync(incidentReportId, request, accountId, cancellationToken)).Value;
    }

    public async Task<Response.Incident> CancelAsync(Guid incidentReportId, Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, cancelValidator);
        return (await ChangeLifecycleAsync(incidentReportId, request, accountId, cancellationToken)).Value;
    }

    public async Task<Response.Incident> SelfResolveAsync(Guid incidentReportId, Request.SelfResolveRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, selfResolveValidator);
        return (await ChangeLifecycleAsync(incidentReportId, request, accountId, cancellationToken)).Value;
    }

    public async Task<Response.SeverityResult> RaiseSeverityAsync(Guid incidentReportId, Request.RaiseSeverityRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, severityValidator);
        var result = await ChangeLifecycleAsync(incidentReportId, request, accountId, cancellationToken);
        return new(result.Value, result.PreviousSeverity.ToString());
    }

    private async Task<(Response.Incident Value, IncidentSeverity PreviousSeverity)> ChangeLifecycleAsync(Guid incidentReportId,
        Request.VersionedRequest request, Guid accountId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var actor = await ActorAsync(accountId, ct);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, ct);
                var allowed = request switch
                {
                    Request.ResolveRequest or Request.CancelRequest => actor.Role == EmployeeRole.LOGISTICS_MANAGER,
                    Request.SelfResolveRequest => Crew(actor) && await CrewAssignedAsync(transport.TransportId, actor, ct),
                    Request.RaiseSeverityRequest => actor.Role == EmployeeRole.LOGISTICS_MANAGER ||
                        await CoordinatorAssignedAsync(transport.TransportId, actor, ct) || Crew(actor) && await CrewAssignedAsync(transport.TransportId, actor, ct),
                    _ => false
                };
                if (!allowed) throw new ForbiddenException("The account cannot perform this action on this incident", "FORBIDDEN");
                var selfResolve = request is Request.SelfResolveRequest and not Request.ResolveRequest;
                if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING) || selfResolve && incident.Status != IncidentStatus.REPORTED)
                    throw new UnprocessableEntityException(selfResolve
                        ? "The incident has already been accepted or closed. Ask the logistics manager to close it"
                        : "The incident is already resolved or cancelled", "INVALID_INCIDENT_STATUS");
                if (Truncate(request.LastUpdatedAt!.Value) != Truncate(incident.UpdatedAt))
                    throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
                var costs = Array.Empty<EmergencyCostRequest>();
                if (request is not Request.RaiseSeverityRequest)
                {
                    var costQuery = db.EmergencyCostRequests.Where(cost => cost.IncidentReportId == incidentReportId);
                    await db.LockRowsAsync(costQuery, cost => cost.EmergencyCostRequestId, ct);
                    costs = await costQuery.OrderBy(cost => cost.EmergencyCostRequestId).ToArrayAsync(ct);
                    foreach (var cost in costs) await db.Entry(cost).ReloadAsync(ct);
                }
                var previousSeverity = incident.Severity;
                var now = NextUpdate(incident);
                switch (request)
                {
                    case Request.ResolveRequest resolve:
                        if (transport.Status is not (TransportStatus.IN_TRANSIT or TransportStatus.COMPLETED))
                            throw new ConflictException("The transport must be IN_TRANSIT or COMPLETED to close an incident", "INVALID_TRANSPORT_STATUS");
                        var pending = costs.Where(cost => cost.Status == EmergencyCostStatus.PENDING).ToArray();
                        if (pending.Length > 0)
                            throw new UnprocessableEntityException($"Cannot close the incident while emergency cost request {pending[0].RequestCode} has not been approved or rejected. Please finish the approval first",
                                "UNRESOLVED_EMERGENCY_COSTS", new { pendingRequests = pending.Select(cost => new
                                { cost.EmergencyCostRequestId, cost.RequestCode, Category = cost.Category.ToString(), RequestedAmount = Vnd.ToLong(cost.RequestedAmount) }).ToArray() });
                        Resolve(incident, resolve.ResolutionSummary, now);
                        break;
                    case Request.CancelRequest cancel:
                        var approved = costs.Where(cost => cost.Status == EmergencyCostStatus.APPROVED).ToArray();
                        if (approved.Length > 0)
                            throw new UnprocessableEntityException("An approved emergency cost already exists. Close the incident instead", "HAS_APPROVED_EMERGENCY_COST",
                                new { approvedRequests = approved.Select(cost => new { cost.EmergencyCostRequestId, cost.RequestCode, ApprovedAmount = Whole(cost.ApprovedAmount) }).ToArray() });
                        foreach (var cost in costs.Where(cost => cost.Status == EmergencyCostStatus.PENDING))
                        {
                            cost.Status = EmergencyCostStatus.CANCELLED; cost.CancelledAt = cost.UpdatedAt = now;
                            cost.CancellationReason = cancel.CancellationReason.Trim();
                        }
                        incident.Status = IncidentStatus.CANCELLED; incident.CancelledAt = now; incident.CancellationReason = cancel.CancellationReason.Trim();
                        break;
                    case Request.SelfResolveRequest self:
                        if (transport.Status != TransportStatus.IN_TRANSIT)
                            throw new ConflictException("The transport must be IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
                        var open = costs.Where(cost => cost.Status is EmergencyCostStatus.PENDING or EmergencyCostStatus.APPROVED).ToArray();
                        if (open.Length > 0)
                            throw new UnprocessableEntityException("Incident has an emergency cost request that is pending or approved. The logistics manager must close it.", "HAS_OPEN_EMERGENCY_COSTS",
                                new { requests = open.Select(cost => new { cost.EmergencyCostRequestId, cost.RequestCode, Status = cost.Status.ToString() }).ToArray() });
                        Resolve(incident, self.ResolutionSummary, now);
                        break;
                    case Request.RaiseSeverityRequest raise:
                        var severity = Enum.Parse<IncidentSeverity>(raise.Severity);
                        if (severity == incident.Severity) throw new UnprocessableEntityException("The incident already has this severity", "SEVERITY_UNCHANGED");
                        if (severity < incident.Severity) throw new UnprocessableEntityException("The severity can only be raised, never lowered", "SEVERITY_CANNOT_DECREASE");
                        incident.Severity = severity;
                        incident.Description += $"\n[{AsUtc(now).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)} · {actor.FullName}] {raise.Reason.Trim()}";
                        break;
                }
                incident.UpdatedAt = now;
                Response.Effects? effects = null;
                if (request is Request.RaiseSeverityRequest)
                    effects = await QueueSeverityRaisedAsync(incident, transport, previousSeverity, now, ct);
                else
                    await QueueLifecycleAsync(incident, transport, actor, selfResolve,
                        costs.Where(cost => cost.CancelledAt == now).Select(cost => cost.RequestedByEmployeeId).ToArray(), now, ct);
                await db.SaveChangesAsync(ct);
                var response = await ReadIncidentAsync(incidentReportId, transport, actor, ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return (response with { Effects = effects }, previousSeverity);
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                if (attempt < 2) { db.ChangeTracker.Clear(); continue; }
                throw new ConflictException("The incident was just modified. Please reload the page", "INCIDENT_MODIFIED");
            }
        }
    }

    private static void Resolve(IncidentReport incident, string summary, DateTime now)
    {
        incident.Status = IncidentStatus.RESOLVED; incident.ResolvedAt = now;
        incident.ResolutionSummary = summary.Trim(); incident.EmergencySettlementRequired = false;
    }
}
