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
    private const string CostForbiddenMessage = "You are not allowed to perform this action on emergency costs";

    public async Task<Response.CostRequest> CreateCostAsync(Guid incidentReportId, Request.CreateCostRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        var ct = cancellationToken;
        Validation.ValidateFields(request, createCostValidator);
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var actor = await ActorAsync(accountId, ct);
                var (incident, transport) = await LockIncidentAsync(incidentReportId, ct);
                if (!(Crew(actor) && await CrewAssignedAsync(transport.TransportId, actor, ct) ||
                    await CoordinatorAssignedAsync(transport.TransportId, actor, ct)))
                    throw new ForbiddenException(CostForbiddenMessage, "FORBIDDEN");
                EnsureCostIncidentOpen(incident);
                if (transport.Status != TransportStatus.IN_TRANSIT)
                    throw new ConflictException("Emergency cost can only be requested while the transport is IN_TRANSIT", "INVALID_TRANSPORT_STATUS");
                var now = Truncate(clock.GetUtcNow());
                var cost = new EmergencyCostRequest
                {
                    EmergencyCostRequestId = Guid.NewGuid(), IncidentReport = incident, RequestedByEmployeeId = actor.EmployeeId,
                    RequestCode = await CodeGeneration.NextAsync("ECR", now, code => db.EmergencyCostRequests.AnyAsync(item => item.RequestCode == code, ct)),
                    Category = Enum.Parse<EmergencyCostCategory>(request.Category), Status = EmergencyCostStatus.PENDING,
                    Description = request.Description.Trim(), Justification = request.Justification.Trim(),
                    RequestedAmount = request.RequestedAmount!.Value, Currency = Vnd.Currency, InvoiceRequired = request.InvoiceRequired,
                    ReceiptStatus = request.InvoiceRequired ? EmergencyReceiptStatus.PENDING : EmergencyReceiptStatus.NOT_REQUIRED,
                    RequestedAt = now, CreatedAt = now, UpdatedAt = now
                };
                db.EmergencyCostRequests.Add(cost);
                await AddPhotosAsync(incident, request.PhotoFiles, accountId, now, ct, cost);
                if (actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) incident.Status = IncidentStatus.PENDING;
                incident.UpdatedAt = NextUpdate(incident);
                await QueueCostManagersAsync(cost, incident, NotificationPriority.HIGH,
                    $"[YÊU CẦU DUYỆT CHI PHÍ KHẨN] Mã: {cost.RequestCode}", now, ct);
                await db.SaveChangesAsync(ct);
                var response = await ReadCostAsync(cost.EmergencyCostRequestId, ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return response;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < 19) continue;
                throw new ConflictException("The request could not be saved after concurrent changes. Please retry", "EMERGENCY_COST_MODIFIED");
            }
        }
    }

    public Task<Response.CostRequest> CancelCostAsync(Guid emergencyCostRequestId, Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, cancelValidator);
        return ChangeCostAsync(emergencyCostRequestId, request, accountId, cancellationToken);
    }

    public Task<Response.CostRequest> DecideCostAsync(Guid emergencyCostRequestId, Request.CostDecisionRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.ValidateFields(request, costDecisionValidator);
        return ChangeCostAsync(emergencyCostRequestId, request, accountId, cancellationToken);
    }

    private async Task<Response.CostRequest> ChangeCostAsync(Guid costId, Request.VersionedRequest request, Guid accountId, CancellationToken ct)
    {
        if (costId == Guid.Empty) throw new BadRequestException("Emergency cost request ID must be a non-empty UUID.", "VALIDATION_ERROR");
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginSerializableAsync(ct);
                var actor = await ActorAsync(accountId, ct);
                var incidentId = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.EmergencyCostRequestId == costId)
                    .Select(item => (Guid?)item.IncidentReportId).SingleOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Emergency cost request not found", "EMERGENCY_COST_NOT_FOUND");
                var (incident, _) = await LockIncidentAsync(incidentId, ct);
                await db.LockRowsAsync(db.EmergencyCostRequests.Where(item => item.EmergencyCostRequestId == costId), item => item.EmergencyCostRequestId, ct);
                var cost = await db.EmergencyCostRequests.SingleAsync(item => item.EmergencyCostRequestId == costId, ct);
                await db.Entry(cost).ReloadAsync(ct);
                var deciding = request is Request.CostDecisionRequest;
                if (deciding ? actor.Role != EmployeeRole.LOGISTICS_MANAGER :
                    !(Crew(actor) || actor.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) || cost.RequestedByEmployeeId != actor.EmployeeId)
                    throw new ForbiddenException(CostForbiddenMessage, "FORBIDDEN");
                if (deciding)
                {
                    var decision = await db.CostApprovalDecisions.AsNoTracking().Include(item => item.Employee)
                        .SingleOrDefaultAsync(item => item.EmergencyCostRequestId == costId, ct);
                    if (cost.Status != EmergencyCostStatus.PENDING || decision is not null)
                        throw new ConflictException(decision is null ? "This request was already processed. Please reload the page" :
                            $"This request was already processed by {decision.Employee.FullName} at {AsUtc(decision.DecidedAt):O}. Please reload the page",
                            "EMERGENCY_COST_ALREADY_DECIDED", new { decidedBy = decision is null ? null : EmployeeBrief(decision.Employee),
                                decidedAt = decision is null ? (DateTimeOffset?)null : AsUtc(decision.DecidedAt), status = cost.Status.ToString() });
                    EnsureCostIncidentOpen(incident);
                }
                else if (cost.Status != EmergencyCostStatus.PENDING)
                    throw new ConflictException("Only a pending request can be cancelled", "INVALID_EMERGENCY_COST_STATUS");
                if (Truncate(request.LastUpdatedAt!.Value) != Truncate(cost.UpdatedAt))
                    throw new ConflictException("The request was just modified. Please reload the page", "EMERGENCY_COST_MODIFIED");
                var now = Truncate(clock.GetUtcNow());
                if (now <= cost.UpdatedAt) now = cost.UpdatedAt.AddTicks(10);
                if (request is Request.CostDecisionRequest decide)
                {
                    var approve = decide.Decision == "APPROVE";
                    var amount = approve ? decide.ApprovedAmount ?? cost.RequestedAmount : 0;
                    if (approve && (amount <= 0 || amount > cost.RequestedAmount))
                        throw new UnprocessableEntityException("The approved amount must not exceed the requested amount and must be greater than 0", "AMOUNT_EXCEEDS_REQUESTED");
                    if (approve && amount < cost.RequestedAmount && !(decide.DecisionReason?.Trim().Length >= 10))
                        throw new FieldValidationException([new("decisionReason", "Decision reason must contain at least 10 characters after trimming.")]);
                    cost.Status = approve ? EmergencyCostStatus.APPROVED : EmergencyCostStatus.REJECTED;
                    cost.ApprovedAmount = amount;
                    db.CostApprovalDecisions.Add(new CostApprovalDecision
                    {
                        CostApprovalDecisionId = Guid.NewGuid(), EmergencyCostRequestId = costId, EmployeeId = actor.EmployeeId,
                        Decision = approve ? ApprovalDecision.APPROVED : ApprovalDecision.REJECTED,
                        ApprovedAmount = amount, DecisionReason = Text.EmptyToNull(decide.DecisionReason), DecidedAt = now, CreatedAt = now
                    });
                    incident.Status = IncidentStatus.PENDING;
                    var proposer = await db.Employees.AsNoTracking().Active().SingleOrDefaultAsync(employee =>
                        employee.EmployeeId == cost.RequestedByEmployeeId && employee.Account.AccountType == AccountType.EMPLOYEE, ct);
                    if (proposer is not null)
                        QueueCost(cost, incident, proposer.AccountId, NotificationPriority.HIGH,
                            approve ? $"[CHI PHÍ ĐÃ ĐƯỢC DUYỆT] Mã ECR: {cost.RequestCode}" : "Emergency cost request rejected",
                            $"/{(Crew(proposer) ? "crew" : "admin")}/incidents/{incidentId}", now);
                }
                else if (request is Request.CancelRequest cancel)
                {
                    cost.Status = EmergencyCostStatus.CANCELLED; cost.CancelledAt = now; cost.CancellationReason = cancel.CancellationReason.Trim();
                    await QueueCostManagersAsync(cost, incident, NotificationPriority.NORMAL, "Emergency cost request cancelled", now, ct);
                }
                cost.UpdatedAt = now; incident.UpdatedAt = NextUpdate(incident);
                await db.SaveChangesAsync(ct);
                var response = await ReadCostAsync(costId, ct);
                await transaction.CommitAsync(ct);
                committed = true;
                await notifications.FlushAsync(ct);
                return response;
            }
            catch (Exception error) when (!committed && PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < 2) continue;
                throw new ConflictException("The request was just modified. Please reload the page", "EMERGENCY_COST_MODIFIED");
            }
        }
    }

    private static void EnsureCostIncidentOpen(IncidentReport incident)
    {
        if (incident.Status is not (IncidentStatus.REPORTED or IncidentStatus.PENDING))
            throw new UnprocessableEntityException("Cannot create or decide an emergency cost for an incident that is resolved or cancelled", "INVALID_INCIDENT_STATUS");
    }

    private async Task QueueCostManagersAsync(EmergencyCostRequest cost, IncidentReport incident, NotificationPriority priority, string title, DateTime now, CancellationToken ct)
    {
        var managers = await db.Employees.AsNoTracking().Active().Where(employee => employee.Role == EmployeeRole.LOGISTICS_MANAGER &&
            employee.Account.AccountType == AccountType.EMPLOYEE).Select(employee => employee.AccountId).Distinct().ToArrayAsync(ct);
        foreach (var accountId in managers)
            QueueCost(cost, incident, accountId, priority, title, $"/admin/emergency-costs/{cost.EmergencyCostRequestId}", now);
    }

    private void QueueCost(EmergencyCostRequest cost, IncidentReport incident, Guid accountId, NotificationPriority priority, string title, string url, DateTime now) =>
        notifications.Queue(new()
        {
            AccountId = accountId, TransportId = incident.TransportId, Type = NotificationType.EMERGENCY_COST, Priority = priority,
            Title = title, Message = $"{cost.RequestCode}: {cost.Description}\nRequested: {Vnd.Format(cost.RequestedAmount)}" +
                (cost.ApprovedAmount is { } amount ? $"\nApproved: {Vnd.Format(amount)}" : "") +
                (cost.CancellationReason is { } reason ? $"\n{reason}" : ""),
            ReferenceType = nameof(EmergencyCostRequest), ReferenceId = cost.EmergencyCostRequestId, ActionUrl = url, CreatedAt = now
        });
}
