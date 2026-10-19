using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Guid> SubmitQuarantineProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitQuarantineProofRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateQuarantineProofRequest(request);

        await using var transaction = await _db.Database.BeginSerializableAsync(cancellationToken);
        await _db.LockRowsAsync(_db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId),
            item => item.ComplianceDossierId, cancellationToken);

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.BookingHorses)
                        .ThenInclude(item => item.Horse)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        await _db.Entry(dossier).ReloadAsync(cancellationToken);
        if (dossier.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException(
                "Quarantine proof can only be submitted when the ComplianceDossier is APPROVED",
                "DOSSIER_NOT_APPROVED");

        var assignedEmployee = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId && (employee.Role == EmployeeRole.DRIVER || employee.Role == EmployeeRole.ESCORT) && employee.EmployeeTransportPlans.Any(item =>
                    item.TransportPlan.TransportId == dossier.TransportId))
            .Select(employee => new { employee.EmployeeId, employee.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        if (assignedEmployee is null)
            throw new ForbiddenException(
                "You are not assigned to this transport",
                "NOT_ASSIGNED_TO_TRIP");

        var now = UtcNow();
        var proof = new QuarantineProof
        {
            QuarantineProofId = Guid.NewGuid(),
            ComplianceDossierId = dossierId,
            Status = request.IsHoldback
                ? QuarantineProofStatus.Holdback
                : QuarantineProofStatus.PendingVerification,
            SubmittedByEmployeeId = assignedEmployee.EmployeeId,
            LocationId = request.LocationId,
            BcpName = request.BcpName.Trim(),
            SubmittedNote = request.QuickNote?.Trim(),
            InspectionAt = request.InspectionAt is null
                ? null
                : DateTime.SpecifyKind(request.InspectionAt.Value.UtcDateTime, DateTimeKind.Unspecified),
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
                FileName = file.FileName,
                FileUrl = file.FileUrl,
                MimeType = file.MimeType,
                FileSizeBytes = file.SizeBytes,
                SortOrder = index + 1,
                CreatedAt = now
            });
        }

        _db.QuarantineProofs.Add(proof);

        AddQuarantineAudit(
            proof,
            accountId,
            "QUARANTINE_PROOF_SUBMITTED",
            "Quarantine proof submitted",
            now,
            new { proof.BcpName, proof.Status });

        var tsAccountIds = await _db.Employees
            .Active()
            .Where(employee => employee.Role == EmployeeRole.TRANSPORT_SPECIALIST)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        var title = request.IsHoldback
            ? "BCP is holding the transport"
            : "BCP quarantine proof submitted";
        var message = request.IsHoldback
            ? $"{proof.BcpName} is holding the transport or a horse for dossier {dossier.DossierCode} for additional inspection. Review the proof and plan the next steps."
            : $"A quarantine proof for dossier {dossier.DossierCode} ({proof.BcpName}) is waiting for your verification.";

        foreach (var tsAccountId in tsAccountIds)
            AddNotification(tsAccountId, EmployeeRole.TRANSPORT_SPECIALIST, dossier, title, message, now,
                request.IsHoldback ? NotificationPriority.URGENT : NotificationPriority.NORMAL);
        if (request.IsHoldback)
            await QueueClearanceMailAsync(tsAccountIds, dossier, MailService.EmailBodies.ClearanceKind.Bcp, proof.BcpName,
                MailService.EmailBodies.ClearanceOutcome.Held, null, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return proof.QuarantineProofId;
    }

    public async Task<Response.QuarantineProofResponse> GetQuarantineProofAsync(
        Guid accountId,
        Guid proofId,
        CancellationToken cancellationToken = default)
    {
        var proof = await _db.QuarantineProofs
            .AsNoTracking()
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.Transport)
                    .ThenInclude(item => item.Booking)
                        .ThenInclude(item => item.Customer)
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.Transport)
                    .ThenInclude(item => item.Booking)
                        .ThenInclude(item => item.BookingHorses)
                            .ThenInclude(item => item.Horse)
            .Include(item => item.SubmittedByEmployee)
            .Include(item => item.VerifiedByEmployee)
            .Include(item => item.Attachments)
            .SingleOrDefaultAsync(item => item.QuarantineProofId == proofId, cancellationToken);

        if (proof is null)
            throw new NotFoundException("QuarantineProof not found", "QUARANTINE_PROOF_NOT_FOUND");

        var isEmployee = await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId,
            cancellationToken);
        var isOwner = proof.ComplianceDossier.Transport.Booking.Customer.AccountId == accountId;

        if (!isEmployee && !isOwner)
            throw new ForbiddenException("You do not have permission to view this QuarantineProof");

        var bookingHorses = proof.ComplianceDossier.Transport.Booking.BookingHorses;
        var files = new List<Response.ProofFileResponse>();
        foreach (var attachment in proof.Attachments.Where(item => item.QuarantineProofId == proof.QuarantineProofId).OrderBy(item => item.SortOrder))
        {
            var readUrl = await _mediaService.CreateReadUrlAsync(attachment.FileUrl, cancellationToken);
            files.Add(new Response.ProofFileResponse
            {
                FileName = attachment.FileName, FileUrl = readUrl.Url, MimeType = attachment.MimeType,
                FileSizeBytes = attachment.FileSizeBytes, SortOrder = attachment.SortOrder
            });
        }

        return new Response.QuarantineProofResponse
        {
            QuarantineProofId = proof.QuarantineProofId,
            ComplianceDossierId = proof.ComplianceDossierId,
            Status = ToQuarantineStatusName(proof.Status),
            BcpName = proof.BcpName,
            LocationId = proof.LocationId,
            InspectionAt = AsUtc(proof.InspectionAt),
            SubmittedNote = proof.SubmittedNote,
            SubmittedBy = new Response.QuarantineEmployeeResponse
            {
                EmployeeId = proof.SubmittedByEmployeeId,
                FullName = proof.SubmittedByEmployee.FullName
            },
            SubmittedAt = AsUtc(proof.SubmittedAt),
            OfficialVeterinarianName = proof.OfficialVeterinarianName,
            BcpReceiptNumber = proof.BcpReceiptNumber,
            Conditions = proof.Conditions,
            MicrochipChecklist = proof.MicrochipChecklist?.RootElement.Clone(),
            VerifiedBy = proof.VerifiedByEmployee is null
                ? null
                : new Response.QuarantineEmployeeResponse
                {
                    EmployeeId = proof.VerifiedByEmployeeId!.Value,
                    FullName = proof.VerifiedByEmployee.FullName
                },
            VerifiedAt = AsUtc(proof.VerifiedAt),
            VerificationNote = proof.VerificationNote,
            RejectionReason = proof.RejectionReason,
            IsFinalized = proof.IsFinalized,
            FinalizedAt = AsUtc(proof.FinalizedAt),
            ExpectedHorses = bookingHorses.Select(item => new Response.ExpectedHorseResponse
            {
                HorseId = item.HorseId,
                HorseName = item.Horse.HorseName,
                MicrochipNumber = item.Horse.MicrochipNumber
            }).ToList(),
            Files = files
        };
    }

    public async Task<Response.QuarantineProofFinalizeResponse> FinalizeQuarantineProofAsync(
        Guid accountId,
        Guid proofId,
        Request.FinalizeQuarantineProofRequest request,
        CancellationToken cancellationToken = default)
    {
        var action = request.Action.Trim().ToUpperInvariant();
        if (action is not ("FINALIZE" or "REJECT_PROOF"))
            throw new BadRequestException("Action must be FINALIZE or REJECT_PROOF");

        var employee = await _db.Employees
            .Active()
            .Where(item => item.AccountId == accountId && item.Role == EmployeeRole.TRANSPORT_SPECIALIST)
            .Select(item => new { item.EmployeeId, item.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null)
            throw new ForbiddenException("Only an active Transport Specialist can verify quarantine proofs");

        var proof = await _db.QuarantineProofs
            .Include(item => item.ComplianceDossier)
                .ThenInclude(item => item.Transport)
                    .ThenInclude(item => item.Booking)
                        .ThenInclude(item => item.BookingHorses)
                            .ThenInclude(item => item.Horse)
            .Include(item => item.SubmittedByEmployee)
            .SingleOrDefaultAsync(item => item.QuarantineProofId == proofId, cancellationToken);

        if (proof is null)
            throw new NotFoundException("QuarantineProof not found", "QUARANTINE_PROOF_NOT_FOUND");

        if (proof.IsFinalized)
            throw new ConflictException(
                "This quarantine proof is finalized and read-only",
                "RECORD_LOCKED");

        var now = UtcNow();
        var dossier = proof.ComplianceDossier;

        if (action == "REJECT_PROOF")
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                throw new BadRequestException(
                    "rejectionReason is required for REJECT_PROOF",
                    "FIELD_REQUIRED_FOR_ACTION");
            InputRules.MaxLength(request.RejectionReason, Limits.NoteMaxLength, "rejectionReason");

            proof.Status = QuarantineProofStatus.Rejected;
            proof.RejectionReason = request.RejectionReason.Trim();
            proof.UpdatedAt = now;

            AddQuarantineAudit(
                proof,
                accountId,
                "QUARANTINE_RESULT_FINALIZED",
                $"Quarantine proof rejected: {proof.RejectionReason}",
                now,
                new { Action = action, proof.RejectionReason });

            AddNotification(
                proof.SubmittedByEmployee.AccountId,
                proof.SubmittedByEmployee.Role,
                dossier,
                "BCP quarantine proof rejected",
                $"Your quarantine proof for dossier {dossier.DossierCode} ({proof.BcpName}) was rejected. Reason: {proof.RejectionReason}. Please submit a new proof.",
                now,
                NotificationPriority.HIGH);
            await QueueClearanceMailAsync([proof.SubmittedByEmployee.AccountId], dossier, MailService.EmailBodies.ClearanceKind.Bcp,
                proof.BcpName, MailService.EmailBodies.ClearanceOutcome.ProofRejected, proof.RejectionReason, now, cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await _notificationService.FlushAsync(cancellationToken);
            return new Response.QuarantineProofFinalizeResponse
            {
                QuarantineProofId = proof.QuarantineProofId,
                Status = ToQuarantineStatusName(proof.Status),
                IsFinalized = false
            };
        }

        if (string.IsNullOrWhiteSpace(request.OfficialVetName) ||
            string.IsNullOrWhiteSpace(request.Status) ||
            string.IsNullOrWhiteSpace(request.MicrochipChecklist))
            throw new BadRequestException(
                "officialVetName, status, and microchipChecklist are required for FINALIZE",
                "FIELD_REQUIRED_FOR_ACTION");

        InputRules.MaxLength(request.OfficialVetName, 200, "officialVetName");
        InputRules.MaxLength(request.BcpReceiptNo, 100, "bcpReceiptNo");
        InputRules.MaxLength(request.Conditions, Limits.NoteMaxLength, "conditions");

        var status = request.Status.Trim().ToUpperInvariant() switch
        {
            "PASSED" => QuarantineProofStatus.Passed,
            "PASSED_WITH_CONDITIONS" => QuarantineProofStatus.PassedWithConditions,
            "FAILED" => QuarantineProofStatus.Failed,
            _ => (QuarantineProofStatus?)null
        };

        if (status is null)
            throw new BadRequestException("Status must be PASSED, PASSED_WITH_CONDITIONS, or FAILED");

        if (status == QuarantineProofStatus.PassedWithConditions &&
            string.IsNullOrWhiteSpace(request.Conditions))
            throw new BadRequestException(
                "conditions is required for PASSED_WITH_CONDITIONS",
                "FIELD_REQUIRED_FOR_ACTION");

        var checklist = ParseMicrochipChecklist(request.MicrochipChecklist);
        var expectedHorses = dossier.Transport.Booking.BookingHorses.ToList();
        var isComplete = expectedHorses.Count == checklist.Count &&
            checklist.All(item => item.IsMatched) &&
            expectedHorses.All(expected => checklist.Count(item => item.HorseId == expected.HorseId) == 1 &&
                checklist.Any(item =>
                    item.HorseId == expected.HorseId &&
                    string.Equals(item.MicrochipNumber?.Trim(), expected.Horse.MicrochipNumber?.Trim(), StringComparison.Ordinal)));

        if (!isComplete)
            throw new BadRequestException(
                "All horses in this dossier must be checked before finalizing",
                "MICROCHIP_CHECK_INCOMPLETE");

        proof.Status = status.Value;
        proof.OfficialVeterinarianName = request.OfficialVetName.Trim();
        proof.BcpReceiptNumber = request.BcpReceiptNo?.Trim();
        proof.Conditions = request.Conditions?.Trim();
        proof.MicrochipChecklist = JsonSerializer.SerializeToDocument(checklist);
        proof.VerifiedByEmployeeId = employee.EmployeeId;
        proof.VerifiedAt = now;
        proof.IsFinalized = true;
        proof.FinalizedAt = now;
        proof.UpdatedAt = now;

        AddQuarantineAudit(
            proof,
            accountId,
            "QUARANTINE_RESULT_FINALIZED",
            $"Quarantine proof finalized as {status.Value}",
            now,
            new { Action = action, Status = status.Value.ToString(), proof.BcpReceiptNumber });

        var recipients = await _db.Employees
            .Active()
            .Where(item => (item.Role == EmployeeRole.DRIVER ||
                    item.Role == EmployeeRole.ESCORT ||
                    item.Role == EmployeeRole.LOGISTICS_MANAGER ||
                    item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) && (item.Role == EmployeeRole.LOGISTICS_MANAGER ||
                    item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR ||
                    item.EmployeeTransportPlans.Any(plan => plan.TransportPlan.TransportId == dossier.TransportId)))
            .Select(item => new { item.AccountId, item.Role })
            .ToListAsync(cancellationToken);

        var priority = status == QuarantineProofStatus.Failed
            ? NotificationPriority.URGENT
            : NotificationPriority.NORMAL;
        var title = status == QuarantineProofStatus.Failed
            ? "BCP quarantine inspection failed"
            : "BCP quarantine inspection verified";
        var message = status == QuarantineProofStatus.Failed
            ? $"{proof.BcpName} failed the quarantine inspection for dossier {dossier.DossierCode}. Immediate action is required."
            : $"{proof.BcpName} finished the quarantine inspection for dossier {dossier.DossierCode} with result {status.Value.ToString().ToUpperInvariant()}.";

        foreach (var recipient in recipients)
            AddNotification(recipient.AccountId, recipient.Role, dossier, title, message, now, priority);

        if (status == QuarantineProofStatus.Failed)
        {
            AddNotification(
                dossier.Transport.Booking.Customer.AccountId,
                null,
                dossier,
                title,
                message,
                now,
                NotificationPriority.URGENT);
            await QueueClearanceMailAsync(recipients.Select(item => item.AccountId).Append(dossier.Transport.Booking.Customer.AccountId), dossier,
                MailService.EmailBodies.ClearanceKind.Bcp, proof.BcpName, MailService.EmailBodies.ClearanceOutcome.Failed, null, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);
        return new Response.QuarantineProofFinalizeResponse
        {
            QuarantineProofId = proof.QuarantineProofId,
            Status = ToQuarantineStatusName(proof.Status),
            IsFinalized = proof.IsFinalized
        };
    }

    private void AddQuarantineAudit(
        QuarantineProof proof,
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
            TargetEntity = nameof(QuarantineProof),
            TargetId = proof.QuarantineProofId,
            ActorAccountId = accountId,
            Summary = summary,
            Details = JsonSerializer.SerializeToDocument(details),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });
    }

    private static BadRequestException InvalidChecklist() =>
        new("microchipChecklist must be a valid JSON array", "INVALID_MICROCHIP_CHECKLIST");

    private static List<MicrochipChecklistItem> ParseMicrochipChecklist(string value)
    {
        try
        {
            var checklist = JsonSerializer.Deserialize<List<MicrochipChecklistItem>>(
                value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return checklist ?? throw InvalidChecklist();
        }
        catch (JsonException)
        {
            throw InvalidChecklist();
        }
    }

    private static void ValidateQuarantineProofRequest(Request.SubmitQuarantineProofRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BcpName))
            throw new BadRequestException("BCP name is required");
        InputRules.MaxLength(request.BcpName, 200, "BCP name");
        InputRules.MaxLength(request.QuickNote, Limits.NoteMaxLength, "quickNote");

        if (request.IsHoldback && string.IsNullOrWhiteSpace(request.QuickNote))
            throw new BadRequestException(
                "quickNote is required when isHoldback is true",
                "QUICK_NOTE_REQUIRED");

        if (request.Files is null || request.Files.Count == 0)
            throw new BadRequestException(
                "Upload failed or image unclear. Please retake the photo",
                "EVIDENCE_UNCLEAR");

        ValidateProofReferences(request.Files);
    }

    private static string ToQuarantineStatusName(QuarantineProofStatus status) => status switch
    {
        QuarantineProofStatus.PendingVerification => "PENDING_VERIFICATION",
        QuarantineProofStatus.Passed => "PASSED",
        QuarantineProofStatus.PassedWithConditions => "PASSED_WITH_CONDITIONS",
        QuarantineProofStatus.Holdback => "HOLDBACK",
        QuarantineProofStatus.Failed => "FAILED",
        QuarantineProofStatus.Rejected => "REJECTED",
        _ => status.ToString().ToUpperInvariant()
    };

    private sealed class MicrochipChecklistItem
    {
        public Guid HorseId { get; set; }

        public string? MicrochipNumber { get; set; }

        public bool IsMatched { get; set; }
    }
}
