using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.DocumentReviewResultResponse> ApproveDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId, Guid versionId,
        Request.ApproveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginReviewTransactionAsync(cancellationToken);
        try
        {
            await RequireSpecialistAsync(accountId, cancellationToken, "Only Transport Specialists can perform DocumentReview.");
            if (!request.MetadataConfirmed || request.VersionUploadedAt == default || request.DocumentUpdatedAt == default)
                throw new BadRequestException("Confirm metadata and provide VersionUploadedAt and DocumentUpdatedAt from the document detail.");
            if (request.ReviewNote?.Length > 2000 || request.ExpiryExceptionReason?.Length > 2000)
                throw new BadRequestException("ReviewNote and ExpiryExceptionReason must not exceed 2000 characters.");
            var dossier = await ReviewDossierAsync(dossierId, cancellationToken);
            var (requirement, document, version) = await ReviewContextAsync(dossierId, requirementId, versionId, cancellationToken);
            if (requirement.Status != DossierRequirementStatus.PROVIDED || document.Status != ComplianceDocumentStatus.PROVIDED)
                throw new ConflictException("Only PROVIDED ComplianceDocuments and DossierRequirements can be approved.");
            if (ReviewTimestamp(request.VersionUploadedAt) != ReviewTimestamp(version.UploadedAt) ||
                ReviewTimestamp(request.DocumentUpdatedAt) != ReviewTimestamp(document.UpdatedAt))
                throw new ConflictException("The document file or metadata changed. Reload and verify the latest document before approving.", "DOCUMENT_REVIEW_CONFLICT");
            var latestReview = await LatestReviewAsync(versionId, cancellationToken);
            if (latestReview?.Result == DocumentReviewResult.APPROVED)
                throw new ConflictException("This DocumentVersion already has an active approval.");
            if (document.ExpiryDate < dossier.Transport.Booking.RequestedDepartureDate && string.IsNullOrWhiteSpace(request.ExpiryExceptionReason))
                throw new ConflictException("Warning: Expiry date of this DocumentVersion is before the transport departure date. Are you sure you want to approve?", "EXPIRY_EXCEPTION_REQUIRED");
            var response = await AppendReviewAsync(accountId, dossier, requirement, document, version, DocumentReviewResult.APPROVED,
                DossierRequirementStatus.SATISFIED, request.ReviewNote?.Trim(), new
                {
                    request.MetadataConfirmed, request.VersionUploadedAt, request.DocumentUpdatedAt,
                    ExpiryExceptionReason = request.ExpiryExceptionReason?.Trim()
                }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (Exception error) when (ReviewWriteConflict(error))
        {
            throw new ConflictException("The document changed. Reload before recording a DocumentReview.", "DOCUMENT_REVIEW_CONFLICT");
        }
    }

    public async Task<Response.DocumentReviewResultResponse> RevokeDocumentReviewAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Guid versionId, Guid reviewId, Request.RevokeDocumentReviewRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginReviewTransactionAsync(cancellationToken);
        try
        {
            await RequireSpecialistAsync(accountId, cancellationToken, "Only Transport Specialists can perform DocumentReview.");
            ValidateReason(request.Reason);
            if (request.Status is not (DossierRequirementStatus.PROVIDED or DossierRequirementStatus.REJECTED))
                throw new BadRequestException("Status must be PROVIDED or REJECTED when revoking approval.");
            var dossier = await ReviewDossierAsync(dossierId, cancellationToken);
            var (requirement, document, version) = await ReviewContextAsync(dossierId, requirementId, versionId, cancellationToken);
            var approved = await LatestReviewAsync(versionId, cancellationToken);
            if (approved is null || approved.DocumentReviewId != reviewId || approved.Result != DocumentReviewResult.APPROVED ||
                requirement.Status != DossierRequirementStatus.SATISFIED || document.Status != ComplianceDocumentStatus.APPROVED)
                throw new ConflictException("Only the current APPROVED DocumentReview can be revoked.");
            var response = await AppendReviewAsync(accountId, dossier, requirement, document, version, DocumentReviewResult.REVOKED,
                request.Status, request.Reason.Trim(), new { ApprovalReviewId = reviewId, Reason = request.Reason.Trim() }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (Exception error) when (ReviewWriteConflict(error))
        {
            throw new ConflictException("The document changed. Reload before revoking approval.", "DOCUMENT_REVIEW_CONFLICT");
        }
    }

    public async Task<Response.DossierReadinessResponse> MarkReadyForSubmissionAsync(Guid accountId, Guid dossierId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginReviewTransactionAsync(cancellationToken);
        try
        {
            await RequireSpecialistAsync(accountId, cancellationToken);
            var dossier = await ReviewDossierAsync(dossierId, cancellationToken);
            if (await _db.DossierRequirements.AnyAsync(item => item.ComplianceDossierId == dossierId && !item.IsDeleted && item.IsMandatory &&
                item.Status != DossierRequirementStatus.SATISFIED && item.Status != DossierRequirementStatus.WAIVED, cancellationToken))
                throw new ConflictException("All mandatory DossierRequirements must be SATISFIED or WAIVED before marking the dossier READY_FOR_SUBMISSION.");
            var previousStatus = dossier.Status;
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            dossier.Status = ComplianceDossierStatus.READY_FOR_SUBMISSION;
            dossier.ReadyForSubmissionAt = now;
            dossier.UpdatedAt = now;
            _db.AuditDossiers.Add(new AuditDossier
            {
                AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, TargetEntity = nameof(ComplianceDossier), TargetId = dossierId,
                ActorAccountId = accountId, ActionType = "DOSSIER_STATUS_CHANGED", Summary = "Dossier marked READY_FOR_SUBMISSION by Transport Specialist.",
                Visibility = AuditVisibility.Public, CreatedAt = now,
                Details = JsonSerializer.SerializeToDocument(new { PreviousStatus = previousStatus.ToString(), NewStatus = dossier.Status.ToString() })
            });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new Response.DossierReadinessResponse
            {
                ComplianceDossierId = dossierId, Status = dossier.Status.ToString(), ReadyForSubmissionAt = DateTime.SpecifyKind(now, DateTimeKind.Utc)
            };
        }
        catch (Exception error) when (ReviewWriteConflict(error))
        {
            throw new ConflictException("The dossier changed. Reload before updating its status.", "DOSSIER_STATUS_CONFLICT");
        }
    }

    private async Task<ComplianceDossier> ReviewDossierAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        // Updating the key to itself holds a row lock until the transaction ends.
        if (_db.Database.IsRelational())
            await _db.ComplianceDossiers.Where(item => item.ComplianceDossierId == dossierId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ComplianceDossierId, item => item.ComplianceDossierId), cancellationToken);
        var dossiers = _db.ComplianceDossiers.AsQueryable();
        var dossier = await dossiers.AsTracking().Include(item => item.Transport).ThenInclude(item => item.Booking)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        await _db.Entry(dossier).ReloadAsync(cancellationToken);
        if (_db.Entry(dossier).State == EntityState.Detached)
            throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        await _db.Entry(dossier.Transport.Booking).ReloadAsync(cancellationToken);
        if (dossier.Status is not (ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.REJECTED or ComplianceDossierStatus.READY_FOR_SUBMISSION))
            throw new ConflictException("Cannot perform DocumentReview or change readiness after dossier submission to official authorities.");
        return dossier;
    }

    private async Task<(DossierRequirement Requirement, ComplianceDocument Document, DocumentVersion Version)> ReviewContextAsync(
        Guid dossierId, Guid requirementId, Guid versionId, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
            await _db.DossierRequirements.Where(item => item.DossierRequirementId == requirementId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DossierRequirementId, item => item.DossierRequirementId), cancellationToken);
        var requirements = _db.DossierRequirements.AsQueryable();
        var requirement = await requirements.AsTracking().SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId &&
            item.ComplianceDossierId == dossierId && !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        await _db.Entry(requirement).ReloadAsync(cancellationToken);
        if (requirement.IsDeleted || _db.Entry(requirement).State == EntityState.Detached)
            throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        if (_db.Database.IsRelational())
            await _db.ComplianceDocuments.Where(item => item.DossierRequirementId == requirementId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ComplianceDocumentId, item => item.ComplianceDocumentId), cancellationToken);
        var documents = _db.ComplianceDocuments.AsQueryable();
        var document = await documents.AsTracking().SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDocument not found.", "DOCUMENT_NOT_FOUND");
        await _db.Entry(document).ReloadAsync(cancellationToken);
        if (_db.Database.IsRelational())
            await _db.DocumentVersions.Where(item => item.DocumentVersionId == versionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DocumentVersionId, item => item.DocumentVersionId), cancellationToken);
        var versions = _db.DocumentVersions.AsQueryable();
        var version = await versions.AsTracking().SingleOrDefaultAsync(item => item.DocumentVersionId == versionId &&
            item.ComplianceDocumentId == document.ComplianceDocumentId, cancellationToken)
            ?? throw new NotFoundException("DocumentVersion not found.", "DOCUMENT_VERSION_NOT_FOUND");
        await _db.Entry(version).ReloadAsync(cancellationToken);
        if (await _db.DocumentVersions.AnyAsync(item => item.ComplianceDocumentId == document.ComplianceDocumentId && item.VersionNo > version.VersionNo, cancellationToken))
            throw new ConflictException("Only the latest DocumentVersion can be reviewed.");
        if (string.IsNullOrWhiteSpace(version.FileUrl))
            throw new ConflictException("DocumentVersion has no submitted file to review.");
        return (requirement, document, version);
    }

    private async Task<Response.DocumentReviewResultResponse> AppendReviewAsync(Guid accountId, ComplianceDossier dossier,
        DossierRequirement requirement, ComplianceDocument document, DocumentVersion version, DocumentReviewResult result,
        DossierRequirementStatus status, string? comment, object confirmation, CancellationToken cancellationToken)
    {
        var previousStatus = requirement.Status;
        var previousDossierStatus = dossier.Status;
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var review = new DocumentReview
        {
            DocumentReviewId = Guid.NewGuid(), DocumentVersionId = version.DocumentVersionId, ReviewedByAccountId = accountId,
            Result = result, ReviewComment = comment, ReviewedAt = now, CreatedAt = now
        };
        _db.DocumentReviews.Add(review);
        requirement.Status = status;
        requirement.SatisfiedAt = status == DossierRequirementStatus.SATISFIED ? now : null;
        requirement.UpdatedAt = now;
        document.Status = status switch
        {
            DossierRequirementStatus.SATISFIED => ComplianceDocumentStatus.APPROVED,
            DossierRequirementStatus.REJECTED => ComplianceDocumentStatus.REJECTED,
            _ => ComplianceDocumentStatus.PROVIDED
        };
        document.UpdatedByAccountId = accountId;
        document.UpdatedAt = now;
        if (result == DocumentReviewResult.REVOKED && requirement.IsMandatory && dossier.Status == ComplianceDossierStatus.READY_FOR_SUBMISSION)
        {
            dossier.Status = ComplianceDossierStatus.IN_PREPARATION;
            dossier.ReadyForSubmissionAt = null;
        }
        dossier.UpdatedAt = now;
        _db.AuditDossiers.Add(new AuditDossier
        {
            AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossier.ComplianceDossierId, TargetEntity = nameof(DocumentReview),
            TargetId = review.DocumentReviewId, ActorAccountId = accountId, ActionType = $"DOCUMENT_REVIEW_{result}",
            Summary = $"DocumentReview {result}; DossierRequirement updated to {status} by Transport Specialist.",
            Visibility = AuditVisibility.Public, CreatedAt = now, Details = JsonSerializer.SerializeToDocument(new
            {
                version.DocumentVersionId, version.VersionNo, requirement.DossierRequirementId, PreviousRequirementStatus = previousStatus.ToString(),
                NewRequirementStatus = status.ToString(), PreviousDossierStatus = previousDossierStatus.ToString(), NewDossierStatus = dossier.Status.ToString(),
                Confirmation = confirmation
            })
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new Response.DocumentReviewResultResponse
        {
            DocumentVersionId = version.DocumentVersionId, DossierRequirementId = requirement.DossierRequirementId,
            RequirementStatus = status.ToString(), DocumentStatus = document.Status.ToString(), DossierStatus = dossier.Status.ToString(),
            Review = new Response.ReviewResponse
            {
                DocumentReviewId = review.DocumentReviewId, ReviewedByAccountId = accountId, Result = result.ToString(),
                ReviewComment = comment, ReviewedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc)
            }
        };
    }

    private Task<DocumentReview?> LatestReviewAsync(Guid versionId, CancellationToken cancellationToken) =>
        _db.DocumentReviews.AsNoTracking().Where(item => item.DocumentVersionId == versionId)
            .OrderByDescending(item => item.ReviewedAt).ThenByDescending(item => item.CreatedAt).ThenByDescending(item => item.DocumentReviewId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<IDbContextTransaction> BeginReviewTransactionAsync(CancellationToken cancellationToken) => _db.Database.IsRelational()
        ? _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
        : _db.Database.BeginTransactionAsync(cancellationToken);

    private static DateTime ReviewTimestamp(DateTime timestamp) => timestamp.Kind == DateTimeKind.Local
        ? timestamp.ToUniversalTime() : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

    private static bool ReviewWriteConflict(Exception error) => error is DbUpdateConcurrencyException ||
        error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
        error is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
