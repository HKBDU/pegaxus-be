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

    public async Task<Guid> SubmitQuarantineProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitQuarantineProofRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateQuarantineProofRequest(request);

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.BookingHorses)
                        .ThenInclude(item => item.Horse)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

        if (dossier.Status != ComplianceDossierStatus.APPROVED)
            throw new ConflictException(
                "Quarantine proof can only be submitted when the ComplianceDossier is APPROVED",
                "DOSSIER_NOT_APPROVED");

        var assignedEmployee = await _db.Employees
            .Where(employee =>
                employee.AccountId == accountId &&
                (employee.Role == EmployeeRole.DRIVER || employee.Role == EmployeeRole.ESCORT) &&
                employee.IsActive &&
                !employee.IsDeleted &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE &&
                employee.EmployeeTransportPlans.Any(item =>
                    item.TransportPlan.TransportId == dossier.TransportId))
            .Select(employee => new { employee.EmployeeId, employee.FullName })
            .SingleOrDefaultAsync(cancellationToken);

        if (assignedEmployee is null)
            throw new ForbiddenException(
                "You are not assigned to this transport",
                "NOT_ASSIGNED_TO_TRIP");

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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
            var file = request.Files[index];
            var (fileUrl, _) = await _mediaService.UploadFileAsync(file);
            proof.Attachments.Add(new ProofAttachment
            {
                ProofAttachmentId = Guid.NewGuid(),
                FileName = file.FileName,
                FileUrl = fileUrl,
                MimeType = file.ContentType,
                FileSizeBytes = file.Length,
                SortOrder = index + 1,
                CreatedAt = now
            });
        }

        _db.QuarantineProofs.Add(proof);

        var tsAccountIds = await _db.Employees
            .Where(employee =>
                employee.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                employee.IsActive &&
                !employee.IsDeleted &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);

        var title = request.IsHoldback
            ? "BCP quarantine proof holdback reported"
            : "BCP quarantine proof submitted";
        var message = request.IsHoldback
            ? "A BCP is holding the transport or horse for additional inspection."
            : "A BCP quarantine proof is awaiting TS verification.";

        foreach (var tsAccountId in tsAccountIds)
            AddNotification(tsAccountId, dossier, title, now, request.IsHoldback ? NotificationPriority.URGENT : NotificationPriority.NORMAL, message);

        await _db.SaveChangesAsync(cancellationToken);

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

        var isEmployee = await _db.Employees.AnyAsync(employee =>
            employee.AccountId == accountId &&
            employee.IsActive &&
            !employee.IsDeleted &&
            !employee.Account.IsDeleted &&
            employee.Account.Status == AccountStatus.ACTIVE,
            cancellationToken);
        var isOwner = proof.ComplianceDossier.Transport.Booking.Customer.AccountId == accountId;

        if (!isEmployee && !isOwner)
            throw new ForbiddenException("You do not have permission to view this QuarantineProof");

        var bookingHorses = proof.ComplianceDossier.Transport.Booking.BookingHorses;
        return new Response.QuarantineProofResponse
        {
            QuarantineProofId = proof.QuarantineProofId,
            ComplianceDossierId = proof.ComplianceDossierId,
            Status = ToQuarantineStatusName(proof.Status),
            BcpName = proof.BcpName,
            LocationId = proof.LocationId,
            InspectionAt = ToDateTimeOffset(proof.InspectionAt),
            SubmittedNote = proof.SubmittedNote,
            SubmittedBy = new Response.QuarantineEmployeeResponse
            {
                EmployeeId = proof.SubmittedByEmployeeId,
                FullName = proof.SubmittedByEmployee.FullName
            },
            SubmittedAt = ToDateTimeOffset(proof.SubmittedAt)!.Value,
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
            VerifiedAt = ToDateTimeOffset(proof.VerifiedAt),
            VerificationNote = proof.VerificationNote,
            RejectionReason = proof.RejectionReason,
            IsFinalized = proof.IsFinalized,
            FinalizedAt = ToDateTimeOffset(proof.FinalizedAt),
            ExpectedHorses = bookingHorses.Select(item => new Response.ExpectedHorseResponse
            {
                HorseId = item.HorseId,
                HorseName = item.Horse.HorseName,
                MicrochipNumber = item.Horse.MicrochipNumber
            }).ToList(),
            Files = proof.Attachments
                .Where(item => item.QuarantineProofId == proof.QuarantineProofId)
                .OrderBy(item => item.SortOrder)
                .Select(item => new Response.ProofFileResponse
                {
                    FileName = item.FileName,
                    FileUrl = item.FileUrl,
                    MimeType = item.MimeType,
                    FileSizeBytes = item.FileSizeBytes,
                    SortOrder = item.SortOrder
                }).ToList()
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
            .Where(item =>
                item.AccountId == accountId &&
                item.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                item.IsActive &&
                !item.IsDeleted &&
                !item.Account.IsDeleted &&
                item.Account.Status == AccountStatus.ACTIVE)
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

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var dossier = proof.ComplianceDossier;

        if (action == "REJECT_PROOF")
        {
            if (string.IsNullOrWhiteSpace(request.RejectionReason))
                throw new BadRequestException(
                    "rejectionReason is required for REJECT_PROOF",
                    "FIELD_REQUIRED_FOR_ACTION");

            proof.Status = QuarantineProofStatus.Rejected;
            proof.RejectionReason = request.RejectionReason.Trim();
            proof.UpdatedAt = now;

            AddQuarantineAudit(
                proof,
                accountId,
                "QUARANTINE_PROOF_REJECTED",
                $"Quarantine proof rejected: {proof.RejectionReason}",
                now,
                new { Action = action, proof.RejectionReason });

            AddNotification(
                proof.SubmittedByEmployee.AccountId,
                dossier,
                "BCP quarantine proof rejected",
                now,
                NotificationPriority.HIGH,
                "The BCP proof was rejected. Please submit a new proof.");

            await _db.SaveChangesAsync(cancellationToken);
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
            "QUARANTINE_PROOF_FINALIZED",
            $"Quarantine proof finalized as {status.Value}",
            now,
            new { Action = action, Status = status.Value.ToString(), proof.BcpReceiptNumber });

        var accountIds = await _db.Employees
            .Where(item =>
                (item.Role == EmployeeRole.DRIVER ||
                    item.Role == EmployeeRole.ESCORT ||
                    item.Role == EmployeeRole.LOGISTICS_MANAGER ||
                    item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR) &&
                item.IsActive &&
                !item.IsDeleted &&
                !item.Account.IsDeleted &&
                item.Account.Status == AccountStatus.ACTIVE &&
                (item.Role == EmployeeRole.LOGISTICS_MANAGER ||
                    item.Role == EmployeeRole.FLEET_ROUTE_COORDINATOR ||
                    item.EmployeeTransportPlans.Any(plan => plan.TransportPlan.TransportId == dossier.TransportId)))
            .Select(item => item.AccountId)
            .ToListAsync(cancellationToken);

        var priority = status == QuarantineProofStatus.Failed
            ? NotificationPriority.URGENT
            : NotificationPriority.NORMAL;
        var title = status == QuarantineProofStatus.Failed
            ? "BCP quarantine inspection failed"
            : "BCP quarantine inspection verified";
        var message = status == QuarantineProofStatus.Failed
            ? "The BCP quarantine proof was finalized as FAILED. Immediate action is required."
            : $"The BCP quarantine proof was finalized as {status.Value.ToString().ToUpperInvariant()}.";

        foreach (var accountIdToNotify in accountIds.Distinct())
            AddNotification(accountIdToNotify, dossier, title, now, priority, message);

        if (status == QuarantineProofStatus.Failed)
        {
            AddNotification(
                dossier.Transport.Booking.Customer.AccountId,
                dossier,
                title,
                now,
                NotificationPriority.URGENT,
                message);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new Response.QuarantineProofFinalizeResponse
        {
            QuarantineProofId = proof.QuarantineProofId,
            Status = ToQuarantineStatusName(proof.Status),
            IsFinalized = proof.IsFinalized
        };
    }

    public async Task<Guid> SubmitCustomsClearanceProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCustomsProofRequest(request);

        var dossier = await _db.ComplianceDossiers
            .Include(item => item.Transport)
                .ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken);

        if (dossier is null)
            throw new NotFoundException("ComplianceDossier not found", "DOSSIER_NOT_FOUND");

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
            .Where(employee =>
                employee.AccountId == accountId &&
                employee.Role == EmployeeRole.DRIVER &&
                employee.IsActive &&
                !employee.IsDeleted &&
                !employee.Account.IsDeleted &&
                employee.Account.Status == AccountStatus.ACTIVE &&
                employee.EmployeeTransportPlans.Any(item =>
                    item.TransportPlan.TransportId == dossier.TransportId))
            .Select(employee => (Guid?)employee.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (driverId is null)
            throw new ForbiddenException("You are not assigned to this transport", "NOT_ASSIGNED_TO_TRIP");

        var customsLane = ParseCustomsLane(request.CustomsLane);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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
            var file = request.Files[index];
            var (fileUrl, _) = await _mediaService.UploadFileAsync(file);
            proof.Attachments.Add(new ProofAttachment
            {
                ProofAttachmentId = Guid.NewGuid(),
                CustomsClearanceProofId = proof.CustomsClearanceProofId,
                FileName = file.FileName,
                FileUrl = fileUrl,
                MimeType = file.ContentType,
                FileSizeBytes = file.Length,
                SortOrder = index + 1,
                CreatedAt = now
            });
        }

        _db.CustomsClearanceProofs.Add(proof);

        var roles = request.IsHeld
            ? new[] { EmployeeRole.TRANSPORT_SPECIALIST, EmployeeRole.FLEET_ROUTE_COORDINATOR }
            : new[] { EmployeeRole.TRANSPORT_SPECIALIST };
        var accountIds = await _db.Employees
            .Where(employee => roles.Contains(employee.Role) && employee.IsActive && !employee.IsDeleted &&
                !employee.Account.IsDeleted && employee.Account.Status == AccountStatus.ACTIVE)
            .Select(employee => employee.AccountId)
            .ToListAsync(cancellationToken);
        var title = request.IsHeld ? "Customs clearance proof held" : "Customs clearance proof submitted";
        var message = request.IsHeld
            ? "Customs is holding the transport for additional inspection."
            : "A customs clearance proof is awaiting TS verification.";
        var priority = request.IsHeld ? NotificationPriority.URGENT : NotificationPriority.NORMAL;

        foreach (var accountIdToNotify in accountIds.Distinct())
            AddNotification(accountIdToNotify, dossier, title, now, priority, message);

        await _db.SaveChangesAsync(cancellationToken);
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

        var isEmployee = await _db.Employees.AnyAsync(employee => employee.AccountId == accountId &&
            employee.IsActive && !employee.IsDeleted && !employee.Account.IsDeleted &&
            employee.Account.Status == AccountStatus.ACTIVE, cancellationToken);
        var isOwner = proof.ComplianceDossier.Transport.Booking.Customer.AccountId == accountId;
        if (!isEmployee && !isOwner)
            throw new ForbiddenException("You do not have permission to view this CustomsClearanceProof");

        var ataDocument = proof.ComplianceDossier.DossierRequirements
            .Where(item => !item.IsDeleted && item.ComplianceDocument is not null &&
                (item.ComplianceRequirement.RequirementCode.Contains("ATA", StringComparison.OrdinalIgnoreCase) ||
                 item.ComplianceRequirement.RequirementName.Contains("ATA", StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.ComplianceDocument!)
            .FirstOrDefault();

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
            ClearanceAt = ToDateTimeOffset(proof.ClearanceAt),
            SubmittedBy = new Response.CustomsEmployeeResponse
            {
                EmployeeId = proof.SubmittedByEmployeeId,
                FullName = proof.SubmittedByEmployee.FullName
            },
            SubmittedAt = ToDateTimeOffset(proof.SubmittedAt)!.Value,
            VerifiedBy = proof.VerifiedByEmployee is null ? null : new Response.CustomsEmployeeResponse
            {
                EmployeeId = proof.VerifiedByEmployeeId!.Value,
                FullName = proof.VerifiedByEmployee.FullName
            },
            VerifiedAt = ToDateTimeOffset(proof.VerifiedAt),
            VerificationNote = proof.VerificationNote,
            RejectionReason = proof.RejectionReason,
            IsFinalized = proof.IsFinalized,
            FinalizedAt = ToDateTimeOffset(proof.FinalizedAt),
            Files = proof.Attachments.OrderBy(item => item.SortOrder).Select(item => new Response.ProofFileResponse
            {
                FileName = item.FileName,
                FileUrl = item.FileUrl,
                MimeType = item.MimeType,
                FileSizeBytes = item.FileSizeBytes,
                SortOrder = item.SortOrder
            }).ToList()
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

    private static List<MicrochipChecklistItem> ParseMicrochipChecklist(string value)
    {
        try
        {
            var checklist = JsonSerializer.Deserialize<List<MicrochipChecklistItem>>(
                value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return checklist ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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

    private static void ValidateQuarantineProofRequest(Request.SubmitQuarantineProofRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BcpName))
            throw new BadRequestException("BCP name is required");

        if (request.IsHoldback && string.IsNullOrWhiteSpace(request.QuickNote))
            throw new BadRequestException(
                "quickNote is required when isHoldback is true",
                "QUICK_NOTE_REQUIRED");

        if (request.Files.Count == 0)
            throw new BadRequestException(
                "Upload failed or image unclear. Please retake the photo",
                "EVIDENCE_UNCLEAR");

        const long maxFileSize = 5 * 1024 * 1024;
        foreach (var file in request.Files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var validExtension = extension is ".jpg" or ".jpeg" or ".png";
            var validContentType = file.ContentType is "image/jpeg" or "image/png";

            if (file.Length > maxFileSize)
                throw new Pegaxus.Service.Exceptions.Exceptions(
                    "Proof image must not exceed 5 MB",
                    413,
                    "FILE_TOO_LARGE");

            if (!validExtension || !validContentType)
                throw new BadRequestException(
                    "Proof file must be JPG, JPEG, or PNG",
                    "INVALID_FILE_TYPE");
        }
    }

    private static void ValidateCustomsProofRequest(Request.SubmitCustomsClearanceProofRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomsPortName))
            throw new BadRequestException("Customs port name is required");

        if (request.IsHeld && string.IsNullOrWhiteSpace(request.QuickNote))
            throw new BadRequestException(
                "quickNote is required when isHeld is true",
                "QUICK_NOTE_REQUIRED");

        if (request.Files.Count == 0)
            throw new BadRequestException(
                "Image unclear or missing customs stamp. Please retake photo",
                "EVIDENCE_UNCLEAR");

        const long maxFileSize = 5 * 1024 * 1024;
        foreach (var file in request.Files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var validExtension = extension is ".jpg" or ".jpeg" or ".png";
            var validContentType = file.ContentType is "image/jpeg" or "image/png";

            if (file.Length > maxFileSize)
                throw new Pegaxus.Service.Exceptions.Exceptions(
                    "Proof image must not exceed 5 MB",
                    413,
                    "FILE_TOO_LARGE");

            if (!validExtension || !validContentType)
                throw new BadRequestException(
                    "Proof file must be JPG, JPEG, or PNG",
                    "INVALID_FILE_TYPE");
        }
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

    private sealed class MicrochipChecklistItem
    {
        public Guid HorseId { get; set; }

        public string? MicrochipNumber { get; set; }

        public bool IsMatched { get; set; }
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
        NotificationPriority priority = NotificationPriority.NORMAL,
        string? message = null)
    {
        var notificationId = Guid.NewGuid();
        _db.Notifications.Add(new Notification
        {
            NotificationId = notificationId,
            TransportId = dossier.TransportId,
            NotificationType = NotificationType.COMPLIANCE,
            Priority = priority,
            Title = title,
            Message = message ?? $"ComplianceDossier {dossier.DossierCode} has been submitted to an authority.",
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
