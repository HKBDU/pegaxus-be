using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.RoutePlanService;

public sealed partial class Service
{
    public async Task<Response.LegalReviewResultResponse> ReviewLegalAsync(Guid accountId, Guid routePlanId, Request.LegalReviewRequest request, CancellationToken cancellationToken = default)
    {
        var reviewer = await RequireEmployeeAsync(accountId, cancellationToken);
        if (reviewer.Role != EmployeeRole.TRANSPORT_SPECIALIST)
            throw new ForbiddenException("Only Transport Specialists can review Route Plans");
        if (request.LastUpdatedAt == default)
            throw new BadRequestException("lastUpdatedAt is required");
        if (!request.IsFeasible && string.IsNullOrWhiteSpace(request.Note))
            throw new BadRequestException("note is required when isFeasible is false");

        await using var transaction = await db.Database.BeginSerializableOrNoneAsync(cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var header = await db.RoutePlans.AsNoTracking().Where(x => x.RoutePlanId == routePlanId)
                .Select(x => new { x.TransportPlanId, x.TransportPlan.TransportId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
            await db.LockRowsAsync(db.Transports.Where(x => x.TransportId == header.TransportId), x => x.TransportId, cancellationToken);
            await db.LockRowsAsync(db.TransportPlans.Where(x => x.TransportPlanId == header.TransportPlanId), x => x.TransportPlanId, cancellationToken);
            await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == routePlanId), x => x.RoutePlanId, cancellationToken);
            await db.LockRowsAsync(db.ComplianceDossiers.Where(x => x.TransportId == header.TransportId), x => x.ComplianceDossierId, cancellationToken);
        }
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsRelational())
        {
            await db.Entry(routePlan).ReloadAsync(cancellationToken);
            await db.Entry(routePlan.TransportPlan.Transport).ReloadAsync(cancellationToken);
            if (routePlan.TransportPlan.Transport.ComplianceDossier is { } currentDossier)
                await db.Entry(currentDossier).ReloadAsync(cancellationToken);
        }
        if (GetRouteType(routePlan) == RouteType.RETURN && routePlan.TransportPlan.Transport.Status != TransportStatus.IN_TRANSIT)
            throw new ConflictException("The transport is not in transit", "TRANSPORT_NOT_IN_TRANSIT");
        if (routePlan.Status != RoutePlanStatus.DRAFT)
            throw new ConflictException($"This action cannot be performed while the route plan is in {routePlan.Status} status", "INVALID_ROUTE_PLAN_STATUS");
        if (!routePlan.TransportPlan.EmployeeTransportPlans.Any(item => item.EmployeeId == reviewer.EmployeeId && item.Employee.Role == EmployeeRole.TRANSPORT_SPECIALIST))
            throw new ForbiddenException("Only the Transport Specialist assigned to this Transport Plan can review it");
        EnsureConcurrency(routePlan.UpdatedAt, request.LastUpdatedAt);
        var routeType = GetRouteType(routePlan);
        var validSequences = routePlan.RoutePlanRoutes.Select(item => item.SequenceNo).ToHashSet();
        if (request.BlockedSegments.Any(sequence => !validSequences.Contains(sequence)))
            throw new BadRequestException("blockedSegments contains an unknown sequenceNo");

        var dossier = routePlan.TransportPlan.Transport.ComplianceDossier;
        if (dossier is null)
            throw new ConflictException("Compliance Dossier must be initialized before legal review", "DOSSIER_NOT_INITIALIZED");
        if (routeType == RouteType.RETURN && dossier.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("Route Plan RETURN has not been approved by the Logistics Manager", "RETURN_NOT_APPROVED");
        if (request.IsFeasible && dossier.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("Compliance Dossier must be in DRAFT before legal review", "INVALID_DOSSIER_STATUS");

        var now = new DateTime(Math.Max(UtcNowTruncated().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW", routePlan, request, now, "Legal feasibility review recorded.");

        if (request.IsFeasible)
        {
            if (routeType == RouteType.RETURN)
            {
                AddDossierAudit(dossier, reviewer, "ROUTE_RETURN_LEGAL_REVIEW", routePlan, request, now, "Return dossier review started.");
                SetDossierStatus(dossier, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_IN_PREPARATION", routePlan, request, now, "Return dossier moved to IN_PREPARATION.");
                SetDossierStatus(dossier, ComplianceDossierStatus.READY_FOR_SUBMISSION, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_READY_FOR_SUBMISSION", routePlan, request, now, "Return dossier marked ready for submission.");
                SetDossierStatus(dossier, ComplianceDossierStatus.SUBMITTED, now);
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_SUBMITTED", routePlan, request, now, "Return dossier submitted without customer upload.");
                SetDossierStatus(dossier, ComplianceDossierStatus.APPROVED, now);
                dossier.ApprovedAt = now;
                AddDossierAudit(dossier, reviewer, "COMPLIANCE_DOSSIER_APPROVED", routePlan, request, now, "Return dossier approved.");
            }
            else
            {
                SetDossierStatus(dossier, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_APPROVED", routePlan, request, now, "Route Plan is legally feasible.");
            }
            routePlan.Status = RoutePlanStatus.CONFIRMED;
            foreach (var vehicle in routePlan.RoutePlanRoutes.SelectMany(item => item.RoutePlanRouteVehicles).Select(item => item.Vehicle).DistinctBy(item => item.VehicleId))
                if (vehicle.Status != VehicleStatus.IN_USE) vehicle.Status = VehicleStatus.ASSIGNED;
            if (routeType == RouteType.PRIMARY &&
                routePlan.RoutePlanRoutes.Where(item => !item.TransportProviderId.HasValue)
                    .All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue))
                routePlan.TransportPlan.Transport.Status = TransportStatus.READY;
        }
        else
        {
            AddDossierAudit(dossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_REJECTED", routePlan, request, now, "Route Plan requires revision.");
        }
        routePlan.UpdatedAt = now;
        QueueLegalReviewNotifications(routePlan, reviewer, routeType, request, now);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return new Response.LegalReviewResultResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            LegalReview = CreateLegalReviewResponse(request, reviewer, now),
            CustomerUploadEnabled = request.IsFeasible && routeType != RouteType.RETURN,
            PersonnelAssignmentPhase2Enabled = request.IsFeasible && routeType == RouteType.PRIMARY,
            UpdatedAt = AsUtc(routePlan.UpdatedAt)
        };
    }

    private async Task<Response.LegalReviewResponse?> GetLatestLegalReviewAsync(Guid routePlanId, CancellationToken cancellationToken)
    {
        var audit = await db.AuditDossiers.AsNoTracking()
            .Where(item => item.TargetEntity == "RoutePlan" && item.TargetId == routePlanId && item.ActionType == "ROUTE_PLAN_LEGAL_REVIEW")
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (audit?.Details is null)
            return null;
        var snapshot = audit.Details.Deserialize<LegalReviewSnapshot>();
        if (snapshot is null)
            return null;
        var reviewer = await db.Employees.AsNoTracking().SingleOrDefaultAsync(item => item.EmployeeId == snapshot.ReviewedByEmployeeId, cancellationToken);
        if (reviewer is null)
            return null;
        return new Response.LegalReviewResponse
        {
            IsFeasible = snapshot.IsFeasible,
            Note = snapshot.Note,
            BlockedSegments = snapshot.BlockedSegments ?? [],
            ReviewedBy = new Response.ReviewedEmployeeResponse
            {
                EmployeeId = reviewer.EmployeeId,
                EmployeeCode = reviewer.EmployeeCode,
                FullName = reviewer.FullName,
                Role = reviewer.Role.ToString()
            },
            ReviewedAt = AsUtc(snapshot.ReviewedAt)
        };
    }

    private static Response.LegalReviewResponse CreateLegalReviewResponse(Request.LegalReviewRequest request, Employee reviewer, DateTime reviewedAt) => new()
    {
        IsFeasible = request.IsFeasible,
        Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        BlockedSegments = request.BlockedSegments.Distinct().OrderBy(item => item).ToList(),
        ReviewedBy = new Response.ReviewedEmployeeResponse
        {
            EmployeeId = reviewer.EmployeeId,
            EmployeeCode = reviewer.EmployeeCode,
            FullName = reviewer.FullName,
            Role = reviewer.Role.ToString()
        },
        ReviewedAt = AsUtc(reviewedAt)
    };

    private void AddDossierAudit(ComplianceDossier dossier, Employee reviewer, string action, RoutePlan routePlan, Request.LegalReviewRequest request, DateTime now, string summary)
    {
        db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(),
            ComplianceDossierId = dossier.ComplianceDossierId,
            ActionType = action,
            TargetEntity = "RoutePlan",
            TargetId = routePlan.RoutePlanId,
            ActorAccountId = reviewer.AccountId,
            Summary = summary,
            Details = JsonSerializer.SerializeToDocument(new LegalReviewSnapshot
            {
                ReviewedByEmployeeId = reviewer.EmployeeId,
                IsFeasible = request.IsFeasible,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                BlockedSegments = request.BlockedSegments.Distinct().OrderBy(item => item).ToList(),
                ReviewedAt = now
            }),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });
    }

    private static void SetDossierStatus(ComplianceDossier dossier, ComplianceDossierStatus status, DateTime now)
    {
        dossier.Status = status;
        dossier.UpdatedAt = now;
        if (status == ComplianceDossierStatus.IN_PREPARATION)
            dossier.OpenedAt = dossier.OpenedAt == default ? now : dossier.OpenedAt;
        if (status == ComplianceDossierStatus.READY_FOR_SUBMISSION)
            dossier.ReadyForSubmissionAt = now;
    }

    private void QueueLegalReviewNotifications(RoutePlan routePlan, Employee reviewer, RouteType routeType, Request.LegalReviewRequest request, DateTime now)
    {
        var recipients = new HashSet<Guid>();
        if (request.IsFeasible)
        {
            if (routeType != RouteType.RETURN)
                recipients.Add(routePlan.TransportPlan.Transport.Booking.Customer.AccountId);
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans
                         .Where(item => item.Employee.Role is EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.LOGISTICS_MANAGER)
                         .Select(item => item.Employee))
                recipients.Add(employee.AccountId);
            if (routeType == RouteType.RETURN)
                foreach (var employee in db.Employees.Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER && item.IsActive && !item.IsDeleted))
                    recipients.Add(employee.AccountId);
        }
        else
        {
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans.Where(item => item.Employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR).Select(item => item.Employee))
                recipients.Add(employee.AccountId);
        }

        foreach (var recipient in recipients.Where(item => item != reviewer.AccountId))
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipient,
                TransportId = routePlan.TransportPlan.TransportId,
                Type = request.IsFeasible ? NotificationType.ROUTE : NotificationType.COMPLIANCE,
                Priority = request.IsFeasible ? NotificationPriority.NORMAL : NotificationPriority.HIGH,
                Title = request.IsFeasible ? "Route Plan legal review confirmed" : "Route Plan requires revision",
                Message = request.IsFeasible
                    ? routeType == RouteType.RETURN
                        ? $"Return Route Plan {routePlan.RoutePlanCode} is legally feasible and requires Logistics Manager action."
                        : "The route has been confirmed. Please upload the required legal documents."
                    : $"Route Plan {routePlan.RoutePlanCode} requires revision. {request.Note}",
                ReferenceType = "RoutePlan",
                ReferenceId = routePlan.RoutePlanId,
                ActionUrl = $"/admin/route-plans/{routePlan.RoutePlanId}",
                CreatedAt = now
            });
    }

    private sealed class LegalReviewSnapshot
    {
        public Guid ReviewedByEmployeeId { get; set; }
        public bool IsFeasible { get; set; }
        public string? Note { get; set; }
        public List<int>? BlockedSegments { get; set; }
        public DateTime ReviewedAt { get; set; }
    }
}
