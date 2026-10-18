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
    public async Task<Response.AuthoritySubmissionListResponse> GetAuthoritySubmissionsAsync(
        Guid accountId,
        Guid dossierId,
        CancellationToken cancellationToken = default)
    {
        var dossier = await _db.ComplianceDossiers
            .AsNoTracking()
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        var isEmployee = await _db.Employees.Active().AnyAsync(employee => employee.AccountId == accountId,
            cancellationToken);

        var isOwner = dossier.Transport.Booking.Customer.AccountId == accountId;
        if (!isEmployee && !isOwner)
            throw new ForbiddenException("You do not have permission to view this ComplianceDossier");

        var submissions = await _db.AuthoritySubmissions
            .AsNoTracking()
            .Where(item => item.ComplianceDossierId == dossierId)
            .Include(item => item.SubmittedByAccount)
                .ThenInclude(item => item.CustomerAccount)
            .Include(item => item.SubmittedByAccount)
                .ThenInclude(item => item.EmployeeAccount)
            .Include(item => item.EvidenceFiles)
            .OrderByDescending(item => item.SubmissionNo)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        var readUrls = new Dictionary<Guid, string>();
        foreach (var file in submissions.SelectMany(item => item.EvidenceFiles))
            readUrls[file.AuthoritySubmissionEvidenceFileId] = (await _mediaService.CreateReadUrlAsync(file.FileUrl, cancellationToken)).Url;

        return new Response.AuthoritySubmissionListResponse
        {
            Items = submissions.Select(item => new Response.AuthoritySubmissionItemResponse
            {
                AuthoritySubmissionId = item.AuthoritySubmissionId,
                SubmissionNo = item.SubmissionNo,
                AuthorityName = item.AuthorityName,
                SubmissionReference = item.SubmissionReference,
                SubmissionMethod = item.SubmissionMethod,
                Status = item.Status.ToString(),
                IsRequired = item.IsRequired,
                SubmittedAt = AsUtc(item.SubmittedAt),
                ResponseReceivedAt = AsUtc(item.ResponseReceivedAt),
                PermitNumber = item.PermitNumber,
                PermitIssuedDate = item.PermitIssuedDate,
                PermitExpiryDate = item.PermitExpiryDate,
                RevisionDueDate = item.RevisionDueDate,
                DecisionNote = item.DecisionNote,
                RejectionReason = item.RejectionReason,
                SubmittedBy = new Response.SubmittedByResponse
                {
                    AccountId = item.SubmittedByAccountId,
                    FullName = item.SubmittedByAccount.EmployeeAccount?.FullName
                        ?? item.SubmittedByAccount.CustomerAccount?.FullName
                        ?? item.SubmittedByAccount.Email
                },
                EvidenceFiles = item.EvidenceFiles
                    .OrderBy(file => file.CreatedAt)
                    .Select(file => new Response.EvidenceFileResponse
                    {
                        EvidenceType = file.EvidenceType.ToString().ToUpperInvariant(),
                        FileName = file.FileName,
                        FileUrl = readUrls[file.AuthoritySubmissionEvidenceFileId],
                        MimeType = file.MimeType,
                        FileSizeBytes = file.FileSizeBytes,
                        Note = file.Note
                    })
                    .ToList()
            }).ToList()
        };
    }

    public async Task<Response.AuthoritySubmissionResponse> RecordAuthoritySubmissionAsync(
        Guid accountId,
        Guid dossierId,
        Request.RecordAuthoritySubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var employeeId = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId)
            .Select(employee => (Guid?)employee.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            throw new ForbiddenException("Only an active Transport Specialist can record authority submissions");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            // Updating the key to itself holds a row lock until the transaction ends.
        await _db.LockRowsAsync(_db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId), item => item.ComplianceDossierId, cancellationToken);

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        await _db.Entry(dossier).ReloadAsync(cancellationToken);
        var canSubmit = dossier.Status is
            ComplianceDossierStatus.READY_FOR_SUBMISSION or
            ComplianceDossierStatus.SUBMITTED or
            ComplianceDossierStatus.PENDING;

        var requirementsReady = await _db.DossierRequirements
            .Where(item => item.ComplianceDossierId == dossierId && !item.IsDeleted && item.IsMandatory)
            .AllAsync(item => item.Status == DossierRequirementStatus.SATISFIED || item.Status == DossierRequirementStatus.WAIVED, cancellationToken);

        if (!canSubmit || !requirementsReady)
            throw new ConflictException(
                "Cannot submit ComplianceDossier. All mandatory DossierRequirements must be SATISFIED or WAIVED first, and the dossier must be READY_FOR_SUBMISSION (or SUBMITTED/PENDING for an additional authority)",
                "DOSSIER_NOT_READY");

        var normalizedReference = request.SubmissionReference.Trim();
        var duplicateReference = await _db.AuthoritySubmissions
            .AnyAsync(item =>
                item.SubmissionReference == normalizedReference &&
                item.ComplianceDossierId != dossierId,
                cancellationToken);

        if (duplicateReference)
            throw new ConflictException(
                "This submission reference number is already linked to another ComplianceDossier",
                "DUPLICATE_SUBMISSION_REFERENCE");

        var submissionNo = (await _db.AuthoritySubmissions
            .Where(item => item.ComplianceDossierId == dossierId)
            .Select(item => (int?)item.SubmissionNo)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var now = UtcNow();
        var submittedAt = request.SubmittedAt.UtcDateTime;
        submittedAt = DateTime.SpecifyKind(submittedAt, DateTimeKind.Unspecified);

        var submission = new AuthoritySubmission
        {
            AuthoritySubmissionId = Guid.NewGuid(),
            ComplianceDossierId = dossierId,
            SubmittedByAccountId = accountId,
            SubmissionNo = submissionNo,
            AuthorityName = request.AuthorityName.Trim(),
            SubmissionReference = normalizedReference,
            SubmissionMethod = request.SubmissionMethod?.Trim(),
            Status = AuthoritySubmissionStatus.SUBMITTED,
            IsRequired = request.IsRequired,
            SubmittedAt = submittedAt,
            DecisionNote = request.DecisionNote?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        var proofFile = await ConfirmAuthorityFileAsync(accountId, request.ProofFile!, "Proof file", cancellationToken);

        submission.EvidenceFiles.Add(new AuthoritySubmissionEvidenceFile
        {
            AuthoritySubmissionEvidenceFileId = Guid.NewGuid(),
            AuthoritySubmissionId = submission.AuthoritySubmissionId,
            EvidenceType = AuthorityEvidenceType.Other,
            UploadedByEmployeeId = employeeId.Value,
            FileName = proofFile.FileName,
            FileUrl = proofFile.FileUrl,
            MimeType = proofFile.MimeType,
            FileSizeBytes = proofFile.SizeBytes,
            CreatedAt = now
        });

        _db.AuthoritySubmissions.Add(submission);

        if (dossier.Status == ComplianceDossierStatus.READY_FOR_SUBMISSION)
        {
            dossier.Status = ComplianceDossierStatus.SUBMITTED;
        }

        dossier.UpdatedAt = now;

        _db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(),
            ComplianceDossierId = dossierId,
            ActionType = "AUTHORITY_SUBMITTED",
            TargetEntity = nameof(AuthoritySubmission),
            TargetId = submission.AuthoritySubmissionId,
            ActorAccountId = accountId,
            Summary = $"Authority submission recorded for {submission.AuthorityName}",
            Details = JsonSerializer.SerializeToDocument(new
            {
                submission.AuthorityName,
                submission.SubmissionReference,
                submission.SubmissionNo,
                submission.Status,
                DossierStatus = dossier.Status.ToString()
            }),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });

        AddNotification(
            dossier.Transport.Booking.Customer.AccountId,
            null,
            dossier,
            "Dossier submitted to authorities",
            $"Your compliance dossier {dossier.DossierCode} was submitted to {submission.AuthorityName}. We will let you know as soon as the authority decides.",
            now);
        await QueueAuthorityMailAsync([dossier.Transport.Booking.Customer.AccountId], dossier, submission.AuthorityName,
            MailService.EmailBodies.AuthorityMailKind.Submitted, null, now, cancellationToken);

        var logisticsManagerAccountIds = await _db.Employees
            .Active()
            .Where(employee => employee.Role == EmployeeRole.LOGISTICS_MANAGER)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var managerAccountId in logisticsManagerAccountIds)
            AddNotification(managerAccountId, EmployeeRole.LOGISTICS_MANAGER, dossier, "Dossier submitted to authorities",
                $"Compliance dossier {dossier.DossierCode} was submitted to {submission.AuthorityName}.", now);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return new Response.AuthoritySubmissionResponse
        {
            AuthoritySubmissionId = submission.AuthoritySubmissionId,
            SubmissionNo = submission.SubmissionNo,
            Status = submission.Status.ToString(),
            DossierStatus = dossier.Status.ToString()
        };
    }

    public async Task<Response.AuthorityDecisionResponse> RecordAuthorityDecisionAsync(
        Guid accountId,
        Guid submissionId,
        Request.RecordAuthorityDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = ValidateDecisionRequest(request);

        var employeeId = await _db.Employees
            .Active()
            .Where(employee => employee.AccountId == accountId && employee.Role == EmployeeRole.TRANSPORT_SPECIALIST)
            .Select(employee => (Guid?)employee.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            throw new ForbiddenException("Only an active Transport Specialist can record authority decisions");

        var submission = await _db.AuthoritySubmissions
            .Include(item => item.ComplianceDossier)
            .SingleOrDefaultAsync(item => item.AuthoritySubmissionId == submissionId, cancellationToken);

        if (submission is null)
            throw new NotFoundException("Authority submission not found", "SUBMISSION_NOT_FOUND");

        if (submission.Status != AuthoritySubmissionStatus.SUBMITTED)
            throw new ConflictException(
                "This AuthoritySubmission already has a decision and is read-only",
                "SUBMISSION_ALREADY_DECIDED");

        if (request.DecisionFile is null || string.IsNullOrWhiteSpace(request.DecisionFile.PublicId))
            throw new BadRequestException(
                "Please upload the official authority decision document",
                "DECISION_FILE_REQUIRED");

        var decisionFile = await ConfirmAuthorityFileAsync(accountId, request.DecisionFile, "Decision file", cancellationToken);

        var now = UtcNow();
        var responseReceivedAt = now;
        var dossier = submission.ComplianceDossier;

        submission.Status = decision;
        submission.ResponseReceivedAt = responseReceivedAt;
        submission.DecisionNote = request.DecisionNote?.Trim();
        submission.PermitNumber = request.PermitNumber?.Trim();
        submission.PermitIssuedDate = request.PermitIssuedDate;
        submission.PermitExpiryDate = request.PermitExpiryDate;
        submission.RejectionReason = request.RejectionReason?.Trim();
        submission.RevisionDueDate = request.RevisionDueDate;
        submission.UpdatedAt = now;

        _db.AuthoritySubmissionEvidenceFiles.Add(new AuthoritySubmissionEvidenceFile
        {
            AuthoritySubmissionEvidenceFileId = Guid.NewGuid(),
            AuthoritySubmissionId = submission.AuthoritySubmissionId,
            EvidenceType = decision switch
            {
                AuthoritySubmissionStatus.APPROVED => AuthorityEvidenceType.Acceptance,
                AuthoritySubmissionStatus.REJECTED => AuthorityEvidenceType.Rejection,
                _ => AuthorityEvidenceType.AdditionalInformation
            },
            UploadedByEmployeeId = employeeId.Value,
            FileName = decisionFile.FileName,
            FileUrl = decisionFile.FileUrl,
            MimeType = decisionFile.MimeType,
            FileSizeBytes = decisionFile.SizeBytes,
            CreatedAt = now
        });

        if (decision == AuthoritySubmissionStatus.RETURNED)
        {
            dossier.Status = ComplianceDossierStatus.IN_PREPARATION;
        }
        else if (decision == AuthoritySubmissionStatus.REJECTED)
        {
            if (submission.IsRequired)
            {
                dossier.Status = ComplianceDossierStatus.CLOSED;
                dossier.RejectedAt = now;
                dossier.RejectionReason = submission.RejectionReason;
                dossier.ClosedAt = now;
            }
        }
        else
        {
            var requiredSubmissions = await _db.AuthoritySubmissions
                .Where(item =>
                    item.ComplianceDossierId == dossier.ComplianceDossierId &&
                    item.IsRequired &&
                    item.AuthoritySubmissionId != submission.AuthoritySubmissionId)
                .Select(item => item.Status)
                .ToListAsync(cancellationToken);

            dossier.Status = requiredSubmissions.All(status => status == AuthoritySubmissionStatus.APPROVED)
                ? ComplianceDossierStatus.APPROVED
                : ComplianceDossierStatus.PENDING;

            if (dossier.Status == ComplianceDossierStatus.APPROVED)
                dossier.ApprovedAt = now;
        }

        dossier.UpdatedAt = now;

        _db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(),
            ComplianceDossierId = dossier.ComplianceDossierId,
            ActionType = "AUTHORITY_DECISION_UPDATED",
            TargetEntity = nameof(AuthoritySubmission),
            TargetId = submission.AuthoritySubmissionId,
            ActorAccountId = accountId,
            Summary = $"Authority decision {decision} recorded",
            Details = JsonSerializer.SerializeToDocument(new
            {
                Decision = decision.ToString(),
                submission.SubmissionNo,
                submission.IsRequired,
                DossierStatus = dossier.Status.ToString()
            }),
            Visibility = AuditVisibility.Internal,
            CreatedAt = now
        });

        if (decision == AuthoritySubmissionStatus.APPROVED && dossier.Status == ComplianceDossierStatus.APPROVED)
        {
            await NotifyEmployeesAsync(
                dossier,
                [EmployeeRole.LOGISTICS_MANAGER, EmployeeRole.FLEET_ROUTE_COORDINATOR],
                "Dossier approved by authorities",
                $"{submission.AuthorityName} approved compliance dossier {dossier.DossierCode}. All required authority approvals are now in place.",
                NotificationPriority.NORMAL,
                now,
                cancellationToken);
        }
        else if (decision == AuthoritySubmissionStatus.REJECTED && submission.IsRequired)
        {
            var rejectionRecipients = await NotifyEmployeesAsync(
                dossier,
                [EmployeeRole.LOGISTICS_MANAGER],
                "Dossier rejected by an authority",
                $"{submission.AuthorityName} rejected a required submission for compliance dossier {dossier.DossierCode}. Reason: {submission.RejectionReason ?? "not provided"}. The dossier needs action before departure.",
                NotificationPriority.URGENT,
                now,
                cancellationToken);
            await QueueAuthorityMailAsync(rejectionRecipients, dossier, submission.AuthorityName,
                MailService.EmailBodies.AuthorityMailKind.Rejected, submission.RejectionReason, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _notificationService.FlushAsync(cancellationToken);

        return new Response.AuthorityDecisionResponse
        {
            AuthoritySubmissionId = submission.AuthoritySubmissionId,
            SubmissionStatus = submission.Status.ToString(),
            DossierStatus = dossier.Status.ToString()
        };
    }

    private static void ValidateRequest(Request.RecordAuthoritySubmissionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AuthorityName))
            throw new BadRequestException("Authority name is required");

        if (string.IsNullOrWhiteSpace(request.SubmissionReference))
            throw new BadRequestException("Submission reference is required");

        if (request.SubmittedAt == default)
            throw new BadRequestException("Submitted timestamp is required");

        if (request.ProofFile is null || string.IsNullOrWhiteSpace(request.ProofFile.PublicId))
            throw new BadRequestException("Proof file is required", "INVALID_FILE_TYPE");
    }

    private static AuthoritySubmissionStatus ValidateDecisionRequest(Request.RecordAuthorityDecisionRequest request)
    {
        if (!Enum.TryParse<AuthoritySubmissionStatus>(request.Decision, true, out var decision) ||
            decision is not (AuthoritySubmissionStatus.APPROVED or AuthoritySubmissionStatus.REJECTED or AuthoritySubmissionStatus.RETURNED))
            throw new BadRequestException("Decision must be APPROVED, REJECTED, or RETURNED");

        if (decision == AuthoritySubmissionStatus.APPROVED && string.IsNullOrWhiteSpace(request.PermitNumber))
            throw new BadRequestException("Permit number is required", "PERMIT_NUMBER_REQUIRED");

        if (decision == AuthoritySubmissionStatus.REJECTED && string.IsNullOrWhiteSpace(request.RejectionReason))
            throw new BadRequestException("Rejection reason is required", "REJECTION_REASON_REQUIRED");

        if (decision == AuthoritySubmissionStatus.RETURNED && request.RevisionDueDate is null)
            throw new BadRequestException("Revision due date is required", "REVISION_DUE_DATE_REQUIRED");

        return decision;
    }
}
