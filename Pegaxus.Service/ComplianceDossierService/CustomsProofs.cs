using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Guid> SubmitCustomsClearanceProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCustomsProofRequest(request);

        await using var transaction = await _db.Database.BeginSerializableAsync(cancellationToken);
        await _db.LockRowsAsync(_db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId),
            item => item.ComplianceDossierId, cancellationToken);

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        await _db.Entry(dossier).ReloadAsync(cancellationToken);
        if (dossier.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException(
                "Customs proof can only be submitted when the ComplianceDossier is APPROVED",
                "DOSSIER_NOT_APPROVED");

        var latestQuarantineStatus = await _db.QuarantineProofs
            .Where(item => item.ComplianceDossierId == dossierId)
            .OrderByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => (QuarantineProofStatus?)item.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestQuarantineStatus is not (QuarantineProofStatus.Passed or QuarantineProofStatus.PassedWithConditions))
            throw new ConflictException(
                "Customs proof can only be submitted after quarantine has passed",
                "QUARANTINE_NOT_PASSED");

        var driverId = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId && employee.Role == EmployeeRole.DRIVER && employee.EmployeeTransportPlans.Any(item =>
                    item.TransportPlan.TransportId == dossier.TransportId))
            .Select(employee => (Guid?)employee.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (driverId is null)
            throw new ForbiddenException("You are not assigned to this transport", "NOT_ASSIGNED_TO_TRIP");

        var customsLane = ParseCustomsLane(request.CustomsLane);
        var now = UtcNow();
        var proof = new CustomsClearanceProof
        {
            CustomsClearanceProofId = Guid.NewGuid(),
            ComplianceDossierId = dossierId,
            Status = request.IsHeld ? CustomsClearanceProofStatus.Held : CustomsClearanceProofStatus.PendingVerification,
            CustomsLane = customsLane,
            SubmittedByEmployeeId = driverId.Value,
            LocationId = request.LocationId,
            CustomsPortName = request.CustomsPortName.Trim(),
            MrnNumber = request.MrnNumber?.Trim(),
            SubmittedNote = request.QuickNote?.Trim(),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        for (var index = 0; index < request.Files.Count; index++)
        {
            var file = await ConfirmProofImageAsync(accountId, request.Files[index].PublicId, cancellationToken);
            proof.Attachments.Add(new ProofAttachment
            {
                ProofAttachmentId = Guid.NewGuid(),
                CustomsClearanceProofId = proof.CustomsClearanceProofId,
                FileName = file.FileName,
                FileUrl = file.FileUrl,
                MimeType = file.MimeType,
                FileSizeBytes = file.SizeBytes,
                SortOrder = index + 1,
                CreatedAt = now
            });
        }

        _db.CustomsClearanceProofs.Add(proof);

        AddCustomsAudit(
            proof,
            accountId,
            "CUSTOMS_PROOF_SUBMITTED",
            "Customs clearance proof submitted",
            now,
            new { proof.CustomsPortName, proof.Status });

        var roles = request.IsHeld
            ? new[] { EmployeeRole.TRANSPORT_SPECIALIST, EmployeeRole.FLEET_ROUTE_COORDINATOR }
            : new[] { EmployeeRole.TRANSPORT_SPECIALIST };
        var recipients = await _db.Employees
            .Active()
            .Where(employee => roles.Contains(employee.Role))
            .Select(employee => new { employee.AccountId, employee.Role })
            .ToListAsync(cancellationToken);
        var title = request.IsHeld ? "Customs is holding the transport" : "Customs clearance proof submitted";
        var message = request.IsHeld
            ? $"Customs at {proof.CustomsPortName} is holding the transport for dossier {dossier.DossierCode} for additional inspection. Review the proof and plan the next steps."
            : $"A customs clearance proof for dossier {dossier.DossierCode} ({proof.CustomsPortName}) is waiting for your verification.";
        var priority = request.IsHeld ? NotificationPriority.URGENT : NotificationPriority.NORMAL;

        foreach (var recipient in recipients)
            AddNotification(recipient.AccountId, recipient.Role, dossier, title, message, now, priority);
        if (request.IsHeld)
            await QueueClearanceMailAsync(recipients.Select(item => item.AccountId), dossier, MailService.EmailBodies.ClearanceKind.Customs,
                proof.CustomsPortName, MailService.EmailBodies.ClearanceOutcome.Held, null, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return proof.CustomsClearanceProofId;
    }

    public async Task<Response.CustomsClearanceProofResponse> GetCustomsClearanceProofAsync(
        Guid accountId,
        Guid proofId,
        CancellationToken cancellationToken = default)
    {
        var proof = await _db.CustomsClearanceProofs
            .AsNoTracking()
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.Transport)
                    .ThenInclude(item => item.Booking)
                        .ThenInclude(item => item.Customer)
            .Include(item => item.SubmittedByEmployee)
            .Include(item => item.VerifiedByEmployee)
            .Include(item => item.Attachments)
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.DossierRequirements)
                    .ThenInclude(item => item.ComplianceRequirement)
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.DossierRequirements)
                    .ThenInclude(item => item.ComplianceDocument)
            .SingleOrDefaultAsync(item => item.CustomsClearanceProofId == proofId, cancellationToken);

        if (proof is null)
            throw new NotFoundException("CustomsClearanceProof not found", "CUSTOMS_CLEARANCE_PROOF_NOT_FOUND");

        var isEmployee = await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId, cancellationToken);
        var isOwner = proof.ComplianceDossier.Transport.Booking.Customer.AccountId == accountId;
        if (!isEmployee && !isOwner)
            throw new ForbiddenException("You do not have permission to view this CustomsClearanceProof");

        var ataDocument = proof.ComplianceDossier.DossierRequirements
            .Where(item => !item.IsDeleted && item.ComplianceDocument is not null &&
                (item.ComplianceRequirement.RequirementCode.Contains("ATA", StringComparison.OrdinalIgnoreCase) ||
                 item.ComplianceRequirement.RequirementName.Contains("ATA", StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.ComplianceDocument!)
            .FirstOrDefault();

        var files = new List<Response.ProofFileResponse>();
        foreach (var attachment in proof.Attachments.OrderBy(item => item.SortOrder))
        {
            var readUrl = await _mediaService.CreateReadUrlAsync(attachment.FileUrl, cancellationToken);
            files.Add(new Response.ProofFileResponse
            {
                FileName = attachment.FileName, FileUrl = readUrl.Url, MimeType = attachment.MimeType,
                FileSizeBytes = attachment.FileSizeBytes, SortOrder = attachment.SortOrder
            });
        }

        return new Response.CustomsClearanceProofResponse
        {
            CustomsClearanceProofId = proof.CustomsClearanceProofId,
            ComplianceDossierId = proof.ComplianceDossierId,
            Status = ToCustomsStatusName(proof.Status),
            CustomsPortName = proof.CustomsPortName,
            LocationId = proof.LocationId,
            MrnNumber = proof.MrnNumber,
            CustomsLane = proof.CustomsLane is null ? null : ToCustomsLaneName(proof.CustomsLane.Value),
            SubmittedNote = proof.SubmittedNote,
            AtaCarnetExpiryDate = proof.AtaCarnetExpiryDate,
            AtaCarnet = ataDocument is null ? null : new Response.AtaCarnetResponse
            {
                ComplianceDocumentId = ataDocument.ComplianceDocumentId,
                ExpiryDate = ataDocument.ExpiryDate,
                Status = ataDocument.Status.ToString().ToUpperInvariant()
            },
            ClearanceAt = AsUtc(proof.ClearanceAt),
            SubmittedBy = new Response.CustomsEmployeeResponse
            {
                EmployeeId = proof.SubmittedByEmployeeId,
                FullName = proof.SubmittedByEmployee.FullName
            },
            SubmittedAt = AsUtc(proof.SubmittedAt),
            VerifiedBy = proof.VerifiedByEmployee is null ? null : new Response.CustomsEmployeeResponse
            {
                EmployeeId = proof.VerifiedByEmployeeId!.Value,
                FullName = proof.VerifiedByEmployee.FullName
            },
            VerifiedAt = AsUtc(proof.VerifiedAt),
            VerificationNote = proof.VerificationNote,
            RejectionReason = proof.RejectionReason,
            IsFinalized = proof.IsFinalized,
            FinalizedAt = AsUtc(proof.FinalizedAt),
            Files = files
        };
    }

    public async Task<Response.CustomsClearanceProofFinalizeResponse> FinalizeCustomsClearanceProofAsync(
        Guid accountId,
        Guid proofId,
        Request.FinalizeCustomsClearanceProofRequest request,
        CancellationToken cancellationToken = default)
    {
        var action = request.Action.Trim().ToUpperInvariant();
        if (action is not ("FINALIZE" or "REJECT_PROOF"))
            throw new BadRequestException("Action must be FINALIZE or REJECT_PROOF");

        var employee = await _db.Employees
            .Active()
            .Where(item => item.AccountId == accountId && item.Role == EmployeeRole.TRANSPORT_SPECIALIST)
            .Select(item => item.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (employee == Guid.Empty)
            throw new ForbiddenException("Only an active Transport Specialist can verify customs proofs");

        var proof = await _db.CustomsClearanceProofs
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.Transport)
                    .ThenInclude(item => item.Booking)
                        .ThenInclude(item => item.Customer)
            .Include(item => item.SubmittedByEmployee)
            .SingleOrDefaultAsync(item => item.CustomsClearanceProofId == proofId, cancellationToken);

        if (proof is null)
            throw new NotFoundException("CustomsClearanceProof not found", "CUSTOMS_CLEARANCE_PROOF_NOT_FOUND");

        if (proof.IsFinalized)
            throw new ConflictException(
                "This customs proof is finalized and read-only",
                "RECORD_LOCKED");

        var now = UtcNow();
        var dossier = proof.ComplianceDossier;

        if (action == "REJECT_PROOF")
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                throw new BadRequestException(
                    "rejectionReason is required for REJECT_PROOF",
                    "FIELD_REQUIRED_FOR_ACTION");

            proof.Status = CustomsClearanceProofStatus.Rejected;
            proof.RejectionReason = request.RejectionReason.Trim();
            proof.UpdatedAt = now;
            AddCustomsAudit(
                proof,
                accountId,
                "CUSTOMS_RESULT_FINALIZED",
                $"Customs proof rejected: {proof.RejectionReason}",
                now,
                new { Action = action, proof.RejectionReason });
            AddNotification(
                proof.SubmittedByEmployee.AccountId,
                proof.SubmittedByEmployee.Role,
                dossier,
                "Customs clearance proof rejected",
                $"Your customs clearance proof for dossier {dossier.DossierCode} ({proof.CustomsPortName}) was rejected. Reason: {proof.RejectionReason}. Please submit a new proof.",
                now,
                NotificationPriority.HIGH);
            await QueueClearanceMailAsync([proof.SubmittedByEmployee.AccountId], dossier, MailService.EmailBodies.ClearanceKind.Customs,
                proof.CustomsPortName, MailService.EmailBodies.ClearanceOutcome.ProofRejected, proof.RejectionReason, now, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await _notificationService.FlushAsync(cancellationToken);
            return new Response.CustomsClearanceProofFinalizeResponse
            {
                CustomsClearanceProofId = proof.CustomsClearanceProofId,
                Status = ToCustomsStatusName(proof.Status),
                IsFinalized = false
            };
        }

        if (string.IsNullOrWhiteSpace(request.MrnNumber) ||
            string.IsNullOrWhiteSpace(request.CustomsLane) ||
            string.IsNullOrWhiteSpace(request.Status))
            throw new BadRequestException(
                "mrnNumber, customsLane, and status are required for FINALIZE",
                "FIELD_REQUIRED_FOR_ACTION");

        var customsLane = ParseCustomsLane(request.CustomsLane);
        if (customsLane is null)
            throw new BadRequestException(
                "mrnNumber, customsLane, and status are required for FINALIZE",
                "FIELD_REQUIRED_FOR_ACTION");

        var status = request.Status.Trim().ToUpperInvariant() switch
        {
            "CLEARED" => CustomsClearanceProofStatus.Cleared,
            "HELD" => CustomsClearanceProofStatus.Held,
            "REJECTED" => CustomsClearanceProofStatus.Rejected,
            _ => (CustomsClearanceProofStatus?)null
        };

        if (status is null)
            throw new BadRequestException("Status must be CLEARED, HELD, or REJECTED");

        if (status == CustomsClearanceProofStatus.Rejected &&
            string.IsNullOrWhiteSpace(request.RejectionReason))
            throw new BadRequestException(
                "rejectionReason is required when status is REJECTED",
                "FIELD_REQUIRED_FOR_ACTION");

        proof.MrnNumber = request.MrnNumber.Trim();
        proof.CustomsLane = customsLane;
        proof.AtaCarnetExpiryDate = request.AtaCarnetExpiryDate;
        proof.Status = status.Value;
        proof.VerificationNote = request.VerificationNote?.Trim();
        proof.RejectionReason = request.RejectionReason?.Trim();
        proof.VerifiedByEmployeeId = employee;
        proof.VerifiedAt = now;
        proof.IsFinalized = true;
        proof.FinalizedAt = now;
        proof.UpdatedAt = now;

        AddCustomsAudit(
            proof,
            accountId,
            "CUSTOMS_RESULT_FINALIZED",
            $"Customs proof finalized as {status.Value}",
            now,
            new
            {
                Action = action,
                Status = status.Value.ToString(),
                proof.MrnNumber,
                CustomsLane = customsLane.ToString(),
                proof.AtaCarnetExpiryDate
            });

        var targetRoles = status == CustomsClearanceProofStatus.Held
            ? new[] { EmployeeRole.FLEET_ROUTE_COORDINATOR }
            : new[] { EmployeeRole.DRIVER, EmployeeRole.ESCORT, EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR };
        var recipients = await _db.Employees
            .Active()
            .Where(item => targetRoles.Contains(item.Role) && (item.Role == EmployeeRole.LOGISTICS_MANAGER ||
                    item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR ||
                    item.EmployeeTransportPlans.Any(plan => plan.TransportPlan.TransportId == dossier.TransportId)))
            .Select(item => new { item.AccountId, item.Role })
            .ToListAsync(cancellationToken);

        var priority = status == CustomsClearanceProofStatus.Rejected
            ? NotificationPriority.URGENT
            : status == CustomsClearanceProofStatus.Held
                ? NotificationPriority.HIGH
                : NotificationPriority.NORMAL;
        var title = status == CustomsClearanceProofStatus.Cleared
            ? "Customs clearance verified"
            : status == CustomsClearanceProofStatus.Held
                ? "Customs clearance held"
                : "Customs clearance rejected";
        var message = status == CustomsClearanceProofStatus.Cleared
            ? $"Customs at {proof.CustomsPortName} cleared the transport for dossier {dossier.DossierCode}."
            : status == CustomsClearanceProofStatus.Held
                ? $"Customs at {proof.CustomsPortName} is holding the transport for dossier {dossier.DossierCode} for inspection. Coordinate the next steps."
                : $"Customs at {proof.CustomsPortName} rejected clearance for dossier {dossier.DossierCode}. Reason: {proof.RejectionReason ?? "not provided"}. Immediate action is required.";

        foreach (var recipient in recipients)
            AddNotification(recipient.AccountId, recipient.Role, dossier, title, message, now, priority);

        if (status == CustomsClearanceProofStatus.Rejected)
        {
            AddNotification(
                dossier.Transport.Booking.Customer.AccountId,
                null,
                dossier,
                title,
                message,
                now,
                NotificationPriority.URGENT);
        }
        if (status is CustomsClearanceProofStatus.Rejected or CustomsClearanceProofStatus.Held)
        {
            var outcome = status == CustomsClearanceProofStatus.Rejected
                ? MailService.EmailBodies.ClearanceOutcome.Rejected : MailService.EmailBodies.ClearanceOutcome.Held;
            var mailRecipients = recipients.Select(item => item.AccountId);
            if (status == CustomsClearanceProofStatus.Rejected)
                mailRecipients = mailRecipients.Append(dossier.Transport.Booking.Customer.AccountId);
            await QueueClearanceMailAsync(mailRecipients, dossier, MailService.EmailBodies.ClearanceKind.Customs, proof.CustomsPortName,
                outcome, proof.RejectionReason, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return new Response.CustomsClearanceProofFinalizeResponse
        {
            CustomsClearanceProofId = proof.CustomsClearanceProofId,
            Status = ToCustomsStatusName(proof.Status),
            IsFinalized = proof.IsFinalized
        };
    }

    private void AddCustomsAudit(
        CustomsClearanceProof proof,
        Guid accountId,
        string actionType,
        string summary,
        DateTime now,
        object details)
    {
        _db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(),
            ComplianceDossierId = proof.ComplianceDossierId,
            ActionType = actionType,
            TargetEntity = nameof(CustomsClearanceProof),
            TargetId = proof.CustomsClearanceProofId,
            ActorAccountId = accountId,
            Summary = summary,
            Details = JsonSerializer.SerializeToDocument(details),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });
    }

    private static void ValidateCustomsProofRequest(Request.SubmitCustomsClearanceProofRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomsPortName))
            throw new BadRequestException("Customs port name is required");

        if (request.IsHeld && string.IsNullOrWhiteSpace(request.QuickNote))
            throw new BadRequestException(
                "quickNote is required when isHeld is true",
                "QUICK_NOTE_REQUIRED");

        if (request.Files is null || request.Files.Count == 0)
            throw new BadRequestException(
                "Image unclear or missing customs stamp. Please retake photo",
                "EVIDENCE_UNCLEAR");

        ValidateProofReferences(request.Files);
    }

    private static CustomsLane? ParseCustomsLane(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant() switch
        {
            "GREEN_LANE" => CustomsLane.GreenLane,
            "ORANGE_LANE" => CustomsLane.OrangeLane,
            "RED_LANE" => CustomsLane.RedLane,
            _ => throw new BadRequestException(
                "Customs lane must be GREEN_LANE, ORANGE_LANE, or RED_LANE")
        };
    }

    private static string ToCustomsStatusName(CustomsClearanceProofStatus status) => status switch
    {
        CustomsClearanceProofStatus.PendingVerification => "PENDING_VERIFICATION",
        CustomsClearanceProofStatus.Cleared => "CLEARED",
        CustomsClearanceProofStatus.Held => "HELD",
        CustomsClearanceProofStatus.Rejected => "REJECTED",
        _ => status.ToString().ToUpperInvariant()
    };

    private static string ToCustomsLaneName(CustomsLane lane) => lane switch
    {
        CustomsLane.GreenLane => "GREEN_LANE",
        CustomsLane.OrangeLane => "ORANGE_LANE",
        CustomsLane.RedLane => "RED_LANE",
        _ => lane.ToString().ToUpperInvariant()
    };
}
