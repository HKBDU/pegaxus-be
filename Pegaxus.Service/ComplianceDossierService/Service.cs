using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed class Service : IService
{
    private const long MaxProofFileSize = 2 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly MediaService.IService _mediaService;

    public Service(AppDbContext db, MediaService.IService mediaService)
    {
        _db = db;
        _mediaService = mediaService;
    }

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

        var isEmployee = await _db.Employees.AnyAsync(employee =>
            employee.AccountId == accountId &&
            !employee.IsDeleted &&
            employee.IsActive &&
            !employee.Account.IsDeleted &&
            employee.Account.Status == AccountStatus.ACTIVE,
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
                SubmittedAt = ToDateTimeOffset(item.SubmittedAt),
                ResponseReceivedAt = ToDateTimeOffset(item.ResponseReceivedAt),
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
                        FileUrl = file.FileUrl,
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
            .Where(employee =>
                employee.AccountId == accountId &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => (Guid?)employee.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (employeeId is null)
            throw new ForbiddenException("Only an active Transport Specialist can record authority submissions");

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.DossierRequirements)
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        var canSubmit = dossier.Status is
            ComplianceDossierStatus.READY_FOR_SUBMISSION or
            ComplianceDossierStatus.SUBMITTED or
            ComplianceDossierStatus.PENDING;

        var requirementsReady = dossier.DossierRequirements
            .Where(item => !item.IsDeleted && item.IsMandatory)
            .All(item => item.Status is DossierRequirementStatus.SATISFIED or DossierRequirementStatus.WAIVED);

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

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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

        var file = request.ProofFile!;
        var (fileUrl, _) = await _mediaService.UploadFileAsync(file);

        submission.EvidenceFiles.Add(new AuthoritySubmissionEvidenceFile
        {
            AuthoritySubmissionEvidenceFileId = Guid.NewGuid(),
            AuthoritySubmissionId = submission.AuthoritySubmissionId,
            EvidenceType = AuthorityEvidenceType.Other,
            UploadedByEmployeeId = employeeId.Value,
            FileName = file.FileName,
            FileUrl = fileUrl,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
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
            ActionType = "AUTHORITY_SUBMISSION_RECORDED",
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
            dossier,
            "ComplianceDossier submitted",
            now);

        var logisticsManagerAccountIds = await _db.Employees
            .Where(employee =>
                employee.Role == EmployeeRole.LOGISTICS_MANAGER &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var managerAccountId in logisticsManagerAccountIds)
            AddNotification(managerAccountId, dossier, "ComplianceDossier submitted", now);

        await _db.SaveChangesAsync(cancellationToken);

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
            .Where(employee =>
                employee.AccountId == accountId &&
                employee.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
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

        if (request.DecisionFile is null || request.DecisionFile.Length == 0)
            throw new BadRequestException(
                "Please upload the official authority decision document",
                "DECISION_FILE_REQUIRED");

        ValidateDecisionFile(request.DecisionFile);

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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

        var (fileUrl, _) = await _mediaService.UploadFileAsync(request.DecisionFile);
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
            FileName = request.DecisionFile.FileName,
            FileUrl = fileUrl,
            MimeType = request.DecisionFile.ContentType,
            FileSizeBytes = request.DecisionFile.Length,
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
            ActionType = "AUTHORITY_DECISION_RECORDED",
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
                "ComplianceDossier approved",
                NotificationPriority.NORMAL,
                now,
                cancellationToken);
        }
        else if (decision == AuthoritySubmissionStatus.REJECTED && submission.IsRequired)
        {
            await NotifyEmployeesAsync(
                dossier,
                [EmployeeRole.LOGISTICS_MANAGER],
                "ComplianceDossier rejected",
                NotificationPriority.URGENT,
                now,
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

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

        if (request.ProofFile is null || request.ProofFile.Length == 0)
            throw new BadRequestException("Proof file is required", "INVALID_FILE_TYPE");

        if (request.ProofFile.Length > MaxProofFileSize)
            throw new Pegaxus.Service.Exceptions.Exceptions("Proof file must not exceed 2 MB", 413, "FILE_TOO_LARGE");

        var extension = Path.GetExtension(request.ProofFile.FileName).ToLowerInvariant();
        var validExtension = extension is ".pdf" or ".jpg" or ".jpeg" or ".png";
        var validContentType = request.ProofFile.ContentType is "application/pdf" or "image/jpeg" or "image/png";

        if (!validExtension || !validContentType)
            throw new BadRequestException("Proof file must be PDF, JPG, JPEG, or PNG", "INVALID_FILE_TYPE");
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

    private static void ValidateDecisionFile(IFormFile file)
    {
        const long maxFileSize = 2 * 1024 * 1024;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var validExtension = extension is ".pdf" or ".jpg" or ".jpeg" or ".png";
        var validContentType = file.ContentType is "application/pdf" or "image/jpeg" or "image/png";

        if (file.Length > maxFileSize)
            throw new Pegaxus.Service.Exceptions.Exceptions("Decision file must not exceed 2 MB", 413, "FILE_TOO_LARGE");

        if (!validExtension || !validContentType)
            throw new BadRequestException("Decision file must be PDF, JPG, JPEG, or PNG", "INVALID_FILE_TYPE");
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private void AddNotification(
        Guid accountId,
        ComplianceDossier dossier,
        string title,
        DateTime now,
        NotificationPriority priority = NotificationPriority.NORMAL)
    {
        var notificationId = Guid.NewGuid();
        _db.Notifications.Add(new Notification
        {
            NotificationId = notificationId,
            TransportId = dossier.TransportId,
            NotificationType = NotificationType.COMPLIANCE,
            Priority = priority,
            Title = title,
            Message = $"ComplianceDossier {dossier.DossierCode} has been submitted to an authority.",
            ReferenceType = "ComplianceDossier",
            ReferenceId = dossier.ComplianceDossierId,
            ActionUrl = $"/compliance-dossiers/{dossier.ComplianceDossierId}",
            CreatedAt = now
        });
        _db.AccountNotifications.Add(new AccountNotification
        {
            AccountId = accountId,
            NotificationId = notificationId,
            Status = RecipientStatus.PENDING,
            CreatedAt = now
        });
    }

    private async Task NotifyEmployeesAsync(
        ComplianceDossier dossier,
        EmployeeRole[] roles,
        string title,
        NotificationPriority priority,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var accountIds = await _db.Employees
            .Where(employee =>
                roles.Contains(employee.Role) &&
                !employee.IsDeleted &&
                employee.IsActive &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var accountId in accountIds)
            AddNotification(accountId, dossier, title, now, priority);
    }
}
