using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using QuestPDF.Infrastructure;

using Pegaxus.Service.Common;

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
            // Confirming the primary also confirms its DRAFT backups, so every plan of the transport plan is locked in id order.
            var planIds = await db.RoutePlans.Where(x => x.TransportPlanId == header.TransportPlanId)
                .OrderBy(x => x.RoutePlanId).Select(x => x.RoutePlanId).ToArrayAsync(cancellationToken);
            foreach (var id in planIds)
                await db.LockRowsAsync(db.RoutePlans.Where(x => x.RoutePlanId == id), x => x.RoutePlanId, cancellationToken);
            await db.LockRowsAsync(db.ComplianceDossiers.Where(x => x.TransportId == header.TransportId), x => x.ComplianceDossierId, cancellationToken);
        }
        var routePlan = await LoadRoutePlanAsync(routePlanId, cancellationToken);
        if (routePlan is null)
            throw new NotFoundException("Route Plan not found", "ROUTE_PLAN_NOT_FOUND");
        if (db.Database.IsRelational())
        {
            await db.Entry(routePlan).ReloadAsync(cancellationToken);
            await db.Entry(routePlan.TransportPlan.Transport).ReloadAsync(cancellationToken);
            foreach (var currentDossier in routePlan.TransportPlan.Transport.ComplianceDossiers)
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

        var transport = routePlan.TransportPlan.Transport;
        var dossier = routeType == RouteType.RETURN ? transport.ExtraDossier : transport.MainDossier;
        if (routeType == RouteType.PRIMARY && dossier is null)
            throw new ConflictException("Compliance Dossier must be initialized before legal review", "DOSSIER_NOT_INITIALIZED");
        if (routeType == RouteType.RETURN && (dossier is null || dossier.Status != ComplianceDossierStatus.DRAFT))
            throw new ConflictException("Route Plan RETURN has not been approved by the Logistics Manager", "RETURN_NOT_APPROVED");
        if (routeType == RouteType.PRIMARY && request.IsFeasible && dossier!.Status != ComplianceDossierStatus.DRAFT)
            throw new ConflictException("Compliance Dossier must be in DRAFT before legal review", "INVALID_DOSSIER_STATUS");

        var now = new DateTime(Math.Max(UtcNowTruncated().Ticks, routePlan.UpdatedAt.Ticks + 10), DateTimeKind.Unspecified);
        var auditDossier = dossier ?? transport.MainDossier;
        if (auditDossier is not null)
            AddDossierAudit(auditDossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW", routePlan, request, now, "Legal feasibility review recorded.");

        var confirmedBackups = new List<RoutePlan>();
        if (request.IsFeasible)
        {
            if (routeType == RouteType.RETURN)
            {
                SetDossierStatus(dossier!, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier!, reviewer, "ROUTE_RETURN_LEGAL_REVIEW", routePlan, request, now, "Extra dossier moved to IN_PREPARATION for the customer to upload documents.");
            }
            else if (routeType == RouteType.PRIMARY)
            {
                SetDossierStatus(dossier!, ComplianceDossierStatus.IN_PREPARATION, now);
                AddDossierAudit(dossier!, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_APPROVED", routePlan, request, now, "Route Plan is legally feasible.");
                confirmedBackups = routePlan.TransportPlan.RoutePlans
                    .Where(item => item.RoutePlanId != routePlan.RoutePlanId && item.Status == RoutePlanStatus.DRAFT && TransportItinerary.IsBackupPlan(item))
                    .OrderBy(item => item.CreatedAt).ToList();
            }
            routePlan.Status = RoutePlanStatus.CONFIRMED;
            foreach (var backup in confirmedBackups)
            {
                backup.Status = RoutePlanStatus.CONFIRMED;
                backup.UpdatedAt = now;
            }
            foreach (var vehicle in routePlan.RoutePlanRoutes.Concat(confirmedBackups.SelectMany(item => item.RoutePlanRoutes))
                         .SelectMany(item => item.RoutePlanRouteVehicles).Select(item => item.Vehicle).DistinctBy(item => item.VehicleId))
                if (vehicle.Status != VehicleStatus.IN_USE) vehicle.Status = VehicleStatus.ASSIGNED;
            if (routeType == RouteType.PRIMARY &&
                routePlan.RoutePlanRoutes.Where(item => !item.TransportProviderId.HasValue)
                    .All(item => item.DriverEmployeeId.HasValue && item.EscortEmployeeId.HasValue))
                transport.Status = TransportStatus.READY;
        }
        else if (auditDossier is not null)
        {
            AddDossierAudit(auditDossier, reviewer, "ROUTE_PLAN_LEGAL_REVIEW_REJECTED", routePlan, request, now, "Route Plan requires revision.");
        }
        routePlan.UpdatedAt = now;
        await QueueLegalReviewNotificationsAsync(routePlan, reviewer, routeType, request, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        await notificationService.FlushAsync(cancellationToken);

        return new Response.LegalReviewResultResponse
        {
            RoutePlanId = routePlan.RoutePlanId,
            Status = routePlan.Status.ToString(),
            LegalReview = CreateLegalReviewResponse(request, reviewer, now),
            ComplianceDossier = routeType == RouteType.BACKUP || dossier is null ? null : new Response.DossierResponse
            {
                ComplianceDossierId = dossier.ComplianceDossierId,
                DossierCode = dossier.DossierCode,
                DossierType = dossier.DossierType.ToString(),
                Status = dossier.Status.ToString()
            },
            ConfirmedBackupRoutePlanIds = confirmedBackups.Select(item => item.RoutePlanId).ToList(),
            CustomerUploadEnabled = request.IsFeasible && routeType != RouteType.BACKUP,
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

    private async Task QueueLegalReviewNotificationsAsync(RoutePlan routePlan, Employee reviewer, RouteType routeType, Request.LegalReviewRequest request,
        DateTime now, CancellationToken cancellationToken)
    {
        var staff = new Dictionary<Guid, EmployeeRole>();
        var transport = routePlan.TransportPlan.Transport;
        var customer = transport.Booking.Customer.AccountId;
        var customerDossier = routeType == RouteType.RETURN ? transport.ExtraDossier : transport.MainDossier;
        var customerUrl = customerDossier is null
            ? NotificationLinks.Booking(null, transport.BookingId)
            : NotificationLinks.Dossier(null, customerDossier.ComplianceDossierId);
        var notifyCustomer = request.IsFeasible && routeType != RouteType.BACKUP;
        if (request.IsFeasible)
        {
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans
                         .Where(item => item.Employee.Role is EmployeeRole.FLEET_ROUTE_COORDINATOR or EmployeeRole.LOGISTICS_MANAGER)
                         .Select(item => item.Employee))
                staff[employee.AccountId] = employee.Role;
            if (routeType == RouteType.RETURN)
                foreach (var employee in db.Employees.Where(item => item.Role == EmployeeRole.LOGISTICS_MANAGER && item.IsActive && !item.IsDeleted))
                    staff[employee.AccountId] = employee.Role;
        }
        else
        {
            foreach (var employee in routePlan.TransportPlan.EmployeeTransportPlans.Where(item => item.Employee.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR).Select(item => item.Employee))
                staff[employee.AccountId] = employee.Role;
        }

        void Notify(Guid recipient, string? actionUrl, string message) =>
            notificationService.Queue(new NotificationService.NotificationCreateRequest
            {
                AccountId = recipient,
                TransportId = routePlan.TransportPlan.TransportId,
                Type = request.IsFeasible ? NotificationType.ROUTE : NotificationType.COMPLIANCE,
                Priority = request.IsFeasible ? NotificationPriority.NORMAL : NotificationPriority.HIGH,
                Title = request.IsFeasible ? "Route Plan legal review confirmed" : "Route Plan requires revision",
                Message = message,
                ReferenceType = "RoutePlan",
                ReferenceId = routePlan.RoutePlanId,
                ActionUrl = actionUrl,
                CreatedAt = now
            });

        var staffMessage = request.IsFeasible
            ? $"Route plan {routePlan.RoutePlanCode} passed legal review and is confirmed."
            : $"Route plan {routePlan.RoutePlanCode} needs revision after legal review. Reviewer note: {request.Note}";
        foreach (var recipient in staff.Where(item => item.Key != reviewer.AccountId && !(notifyCustomer && item.Key == customer)))
            Notify(recipient.Key, NotificationLinks.RoutePlan(recipient.Value, routePlan.RoutePlanId), staffMessage);
        if (notifyCustomer && customer != reviewer.AccountId)
            Notify(customer, customerUrl, routeType == RouteType.RETURN
                ? $"The return route for booking {transport.Booking.BookingCode} has been confirmed. Please upload the additional documents it requires."
                : $"The route for booking {transport.Booking.BookingCode} has been confirmed. Please upload the required legal documents.");

        if (!request.IsFeasible)
        {
            var revisionKind = MailService.EmailBodies.RouteReviewKind.RevisionRequired;
            await MailService.MailRecipients.QueueAsync(db, staff.Keys.Where(id => id != reviewer.AccountId), now, recipient => MailService.MailRecipients.Mail(recipient,
                $"{MailService.EmailBodies.RouteReviewTitle(revisionKind)} — {routePlan.RoutePlanCode}",
                MailService.EmailBodies.RouteReviewBody(recipient.FullName, routePlan.RoutePlanCode, revisionKind, request.Note,
                    NotificationLinks.RoutePlan(recipient.Role, routePlan.RoutePlanId))), cancellationToken);
        }
        else if (notifyCustomer && customer != reviewer.AccountId)
        {
            var confirmedKind = routeType == RouteType.RETURN
                ? MailService.EmailBodies.RouteChangeKind.ReturnConfirmed : MailService.EmailBodies.RouteChangeKind.RouteConfirmed;
            await MailService.MailRecipients.QueueAsync(db, [customer], now, recipient => MailService.MailRecipients.Mail(recipient,
                $"{MailService.EmailBodies.RouteChangedTitle(confirmedKind)} — {routePlan.RoutePlanCode}",
                MailService.EmailBodies.RouteChangedBody(recipient.FullName, routePlan.RoutePlanCode, confirmedKind, null, customerUrl)), cancellationToken);
        }
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
