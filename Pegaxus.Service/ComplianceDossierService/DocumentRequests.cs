using System.Data;
using System.Net;
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
    public async Task<Response.DocumentRejectionResponse> RejectDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Guid versionId, Request.RejectDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await RequireSpecialistAsync(accountId, cancellationToken);
        ValidateRejection(request);
        var mailService = _mailService ?? throw new InvalidOperationException("MailService must be configured for DocumentRequests.");
        Response.DocumentRejectionResponse response;
        string customerEmail;
        await using (var transaction = await BeginDocumentRequestTransactionAsync(cancellationToken))
        {
            try
            {
                var dossiers = _db.Database.IsRelational()
                    ? _db.ComplianceDossiers.FromSqlInterpolated($"SELECT * FROM \"ComplianceDossier\" WHERE compliance_dossier_id = {dossierId} FOR UPDATE")
                    : _db.ComplianceDossiers.AsQueryable();
                var dossier = await dossiers.AsTracking().Include(item => item.Transport).ThenInclude(item => item.Booking)
                    .ThenInclude(item => item.Customer).ThenInclude(item => item.Account)
                    .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
                    ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
                await _db.Entry(dossier).ReloadAsync(cancellationToken);
                if (dossier.Status is not (ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.READY_FOR_SUBMISSION or ComplianceDossierStatus.REJECTED))
                    throw new ConflictException("Cannot create DocumentRequests after dossier submission to official authorities.");
                var requirements = _db.Database.IsRelational()
                    ? _db.DossierRequirements.FromSqlInterpolated($"SELECT * FROM \"DossierRequirement\" WHERE dossier_requirement_id = {requirementId} FOR UPDATE")
                    : _db.DossierRequirements.AsQueryable();
                var requirement = await requirements.AsTracking().SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId &&
                    item.ComplianceDossierId == dossierId && !item.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
                await _db.Entry(requirement).ReloadAsync(cancellationToken);
                if (requirement.IsDeleted)
                    throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
                var documents = _db.Database.IsRelational()
                    ? _db.ComplianceDocuments.FromSqlInterpolated($"SELECT * FROM \"ComplianceDocument\" WHERE dossier_requirement_id = {requirementId} FOR UPDATE")
                    : _db.ComplianceDocuments.AsQueryable();
                var document = await documents.AsTracking().SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId, cancellationToken)
                    ?? throw new NotFoundException("ComplianceDocument not found.", "DOCUMENT_NOT_FOUND");
                await _db.Entry(document).ReloadAsync(cancellationToken);
                var versions = _db.Database.IsRelational()
                    ? _db.DocumentVersions.FromSqlInterpolated($"SELECT * FROM \"DocumentVersion\" WHERE document_version_id = {versionId} FOR UPDATE")
                    : _db.DocumentVersions.AsQueryable();
                var version = await versions.AsTracking().SingleOrDefaultAsync(item => item.DocumentVersionId == versionId &&
                    item.ComplianceDocumentId == document.ComplianceDocumentId, cancellationToken)
                    ?? throw new NotFoundException("DocumentVersion not found.", "DOCUMENT_VERSION_NOT_FOUND");
                await _db.Entry(version).ReloadAsync(cancellationToken);
                if (document.Status != ComplianceDocumentStatus.PROVIDED || requirement.Status != DossierRequirementStatus.PROVIDED)
                    throw new ConflictException("Only PROVIDED ComplianceDocuments and DossierRequirements can be rejected.");
                if (string.IsNullOrWhiteSpace(version.FileUrl) || await _db.DocumentVersions.AnyAsync(item =>
                    item.ComplianceDocumentId == document.ComplianceDocumentId && item.VersionNo > version.VersionNo, cancellationToken))
                    throw new ConflictException("Only the latest submitted DocumentVersion can be rejected.");
                if (RevisionTimestamp(request.VersionUploadedAt) != RevisionTimestamp(version.UploadedAt) ||
                    RevisionTimestamp(request.DocumentUpdatedAt) != RevisionTimestamp(document.UpdatedAt))
                    throw new ConflictException("The document file or metadata changed. Reload before recording a DocumentReview.", "DOCUMENT_REVIEW_CONFLICT");
                var latestReview = await _db.DocumentReviews.AsNoTracking().Where(item => item.DocumentVersionId == versionId)
                    .OrderByDescending(item => item.ReviewedAt).ThenByDescending(item => item.CreatedAt).ThenByDescending(item => item.DocumentReviewId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (latestReview?.Result == DocumentReviewResult.APPROVED)
                    throw new ConflictException("Revoke the current approval before rejecting this DocumentVersion.");
                var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                var note = request.RejectionNote.Trim();
                var review = new DocumentReview
                {
                    DocumentReviewId = Guid.NewGuid(), DocumentVersionId = versionId, ReviewedByAccountId = accountId,
                    Result = DocumentReviewResult.REJECTED, ReviewComment = note, ReviewedAt = now, CreatedAt = now
                };
                var revision = new DocumentRequest
                {
                    DocumentRequestId = Guid.NewGuid(), DocumentReviewId = review.DocumentReviewId,
                    DossierRequirementId = requirementId, RequestedByAccountId = accountId, RequestCode = $"DRQ-{Guid.NewGuid():N}",
                    RequestType = DocumentRequestType.REUPLOAD, Status = DocumentRequestStatus.OPEN, ErrorCategory = request.ErrorCategory.ToString(),
                    Message = note, DueDate = request.RevisionDeadline, RequestedAt = now, CreatedAt = now, UpdatedAt = now
                };
                var previousRequirementStatus = requirement.Status;
                var previousDossierStatus = dossier.Status;
                requirement.Status = request.RequirementStatus;
                requirement.SatisfiedAt = null;
                requirement.UpdatedAt = now;
                document.Status = ComplianceDocumentStatus.REJECTED;
                document.UpdatedAt = now;
                document.UpdatedByAccountId = accountId;
                dossier.Status = request.DossierStatus;
                dossier.ReadyForSubmissionAt = null;
                dossier.UpdatedAt = now;
                _db.DocumentReviews.Add(review);
                _db.DocumentRequests.Add(revision);
                var details = new
                {
                    review.DocumentReviewId, version.DocumentVersionId, version.VersionNo, revision.DocumentRequestId,
                    revision.ErrorCategory, revision.DueDate, PreviousRequirementStatus = previousRequirementStatus.ToString(),
                    NewRequirementStatus = requirement.Status.ToString(), PreviousDossierStatus = previousDossierStatus.ToString(),
                    NewDossierStatus = dossier.Status.ToString(), request.VersionUploadedAt, request.DocumentUpdatedAt
                };
                AddDocumentRequestAudit(dossierId, accountId, "DOCUMENT_REVIEW_REJECTED", nameof(DocumentReview), review.DocumentReviewId, note, now, details);
                AddDocumentRequestAudit(dossierId, accountId, "DOCUMENT_REQUEST_CREATED", nameof(DocumentRequest), revision.DocumentRequestId, note, now, details);
                AddNotification(dossier.Transport.Booking.Customer.AccountId, dossier, "DocumentRequest: re-upload required", now,
                    message: $"{revision.RequestCode}: {note} Revision deadline: {request.RevisionDeadline:yyyy-MM-dd}.");
                customerEmail = dossier.Transport.Booking.Customer.Account.Email;
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                response = new Response.DocumentRejectionResponse
                {
                    Review = new Response.ReviewResponse
                    {
                        DocumentReviewId = review.DocumentReviewId, ReviewedByAccountId = accountId, Result = "REJECTED",
                        ReviewComment = note, ReviewedAt = RevisionTimestamp(now)
                    },
                    DocumentRequest = MapDocumentRequest(revision), RequirementStatus = requirement.Status.ToString(),
                    DocumentStatus = document.Status.ToString(), DossierStatus = dossier.Status.ToString()
                };
            }
            catch (Exception error) when (DocumentRequestWriteConflict(error))
            {
                throw new ConflictException("The document changed. Reload before creating a DocumentRequest.", "DOCUMENT_REVIEW_CONFLICT");
            }
        }
        await _notificationService.FlushAsync(CancellationToken.None);
        try
        {
            var revision = response.DocumentRequest;
            await mailService.SendMail(new MailService.MailContent
            {
                To = customerEmail, Subject = $"DocumentRequest {revision.RequestCode}: re-upload required",
                Body = $"<p>{WebUtility.HtmlEncode(revision.ErrorCategory)}</p><p>{WebUtility.HtmlEncode(revision.Message).Replace("\n", "<br>")}</p><p>Revision deadline: {revision.DueDate:yyyy-MM-dd}</p>"
            }).WaitAsync(TimeSpan.FromSeconds(2));
            response.EmailSent = true;
        }
        catch (Exception error)
        {
            response.DeliveryWarning = "Email delivery could not be confirmed. The DocumentRequest and web notification were saved.";
            Console.Error.WriteLine($"DocumentRequest {response.DocumentRequest.DocumentRequestId}: email delivery failed ({error.GetType().Name}).");
        }
        return response;
    }

    public async Task<Response.DocumentRequestCommentResponse> AddDocumentRequestCommentAsync(Guid accountId, Guid dossierId,
        Guid requirementId, Guid requestId, Request.DocumentRequestCommentRequest request, CancellationToken cancellationToken = default)
    {
        var (dossier, booking, role) = await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        if (role is not (null or EmployeeRole.TRANSPORT_SPECIALIST))
            throw new ForbiddenException("Only the owning Customer and Transport Specialists can discuss DocumentRequests.");
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length > 2000)
            throw new BadRequestException("Comment must contain 1 to 2000 characters.");
        await using var transaction = await BeginDocumentRequestTransactionAsync(cancellationToken);
        try
        {
            var revision = await FindDocumentRequestAsync(dossierId, requirementId, requestId, true, cancellationToken);
            await _db.Entry(revision).ReloadAsync(cancellationToken);
            if (revision.Status != DocumentRequestStatus.OPEN)
                throw new ConflictException("Only OPEN DocumentRequests accept comments.");
            var customerAccountId = await _db.Customers.Where(item => item.CustomerId == booking.CustomerId)
                .Select(item => item.AccountId).SingleAsync(cancellationToken);
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var comment = new DocumentRequestComment
            {
                DocumentRequestCommentId = Guid.NewGuid(), DocumentRequestId = requestId,
                AuthorAccountId = accountId, Message = request.Message.Trim(), CreatedAt = now
            };
            _db.DocumentRequestComments.Add(comment);
            AddDocumentRequestAudit(dossierId, accountId, "DOCUMENT_REQUEST_COMMENTED", nameof(DocumentRequestComment),
                comment.DocumentRequestCommentId, "Comment added to DocumentRequest.", now, new { DocumentRequestId = requestId });
            var recipientId = role is null ? revision.RequestedByAccountId : customerAccountId;
            if (recipientId != accountId)
                AddNotification(recipientId, dossier, "New DocumentRequest comment", now, message: $"{revision.RequestCode}: {comment.Message}");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await _notificationService.FlushAsync(CancellationToken.None);
            return MapDocumentRequestComment(comment);
        }
        catch (Exception error) when (DocumentRequestWriteConflict(error))
        {
            throw new ConflictException("The DocumentRequest changed. Reload before commenting.", "DOCUMENT_REQUEST_CONFLICT");
        }
    }

    public async Task<IReadOnlyList<Response.DocumentRequestCommentResponse>> GetDocumentRequestCommentsAsync(Guid accountId,
        Guid dossierId, Guid requirementId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var (_, _, role) = await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        if (role is not (null or EmployeeRole.TRANSPORT_SPECIALIST))
            throw new ForbiddenException("Only the owning Customer and Transport Specialists can discuss DocumentRequests.");
        await FindDocumentRequestAsync(dossierId, requirementId, requestId, false, cancellationToken);
        var comments = await _db.DocumentRequestComments.AsNoTracking().Where(item => item.DocumentRequestId == requestId)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.DocumentRequestCommentId).ToListAsync(cancellationToken);
        return comments.Select(MapDocumentRequestComment).ToList();
    }

    private async Task<DocumentRequest> FindDocumentRequestAsync(Guid dossierId, Guid requirementId, Guid requestId,
        bool forUpdate, CancellationToken cancellationToken)
    {
        var requests = forUpdate && _db.Database.IsRelational()
            ? _db.DocumentRequests.FromSqlInterpolated($"SELECT * FROM \"DocumentRequest\" WHERE document_request_id = {requestId} FOR UPDATE")
            : _db.DocumentRequests.AsQueryable();
        return await requests.SingleOrDefaultAsync(item => item.DocumentRequestId == requestId && item.DossierRequirementId == requirementId &&
            item.DossierRequirement.ComplianceDossierId == dossierId && !item.DossierRequirement.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DocumentRequest not found.", "DOCUMENT_REQUEST_NOT_FOUND");
    }

    private static void ValidateRejection(Request.RejectDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RejectionNote) || request.RejectionNote.Trim().Length < 15)
            throw new BadRequestException("Please provide detailed instructions in the DocumentRequest (minimum 15 characters) so the customer knows what to re-upload.");
        if (request.RejectionNote.Trim().Length > 2000)
            throw new BadRequestException("RejectionNote must not exceed 2000 characters.");
        if (!Enum.IsDefined(request.ErrorCategory))
            throw new BadRequestException("ErrorCategory is invalid.");
        if (request.RevisionDeadline == default || request.RevisionDeadline < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BadRequestException("RevisionDeadline is required and cannot be in the past.");
        if (request.RequirementStatus is not (DossierRequirementStatus.REJECTED or DossierRequirementStatus.REQUESTED) ||
            request.DossierStatus is not (ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.REJECTED))
            throw new BadRequestException("Choose REJECTED or REQUESTED for the requirement and IN_PREPARATION or REJECTED for the dossier.");
        if (request.VersionUploadedAt == default || request.DocumentUpdatedAt == default)
            throw new BadRequestException("Provide VersionUploadedAt and DocumentUpdatedAt from the requirement detail.");
    }

    private void AddDocumentRequestAudit(Guid dossierId, Guid actorId, string action, string entity, Guid targetId,
        string summary, DateTime now, object details) => _db.AuditDossiers.Add(new AuditDossier
    {
        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, ActorAccountId = actorId, ActionType = action,
        TargetEntity = entity, TargetId = targetId, Summary = summary, Visibility = AuditVisibility.Public,
        CreatedAt = now, Details = JsonSerializer.SerializeToDocument(details)
    });

    private static Response.DocumentRequestResponse MapDocumentRequest(DocumentRequest request) => new()
    {
        DocumentRequestId = request.DocumentRequestId, DocumentReviewId = request.DocumentReviewId,
        DossierRequirementId = request.DossierRequirementId, RequestedByAccountId = request.RequestedByAccountId,
        RequestCode = request.RequestCode, Status = request.Status.ToString(), ErrorCategory = request.ErrorCategory,
        Message = request.Message, DueDate = request.DueDate, RequestedAt = RevisionTimestamp(request.RequestedAt)
    };

    private static Response.DocumentRequestCommentResponse MapDocumentRequestComment(DocumentRequestComment comment) => new()
    {
        DocumentRequestCommentId = comment.DocumentRequestCommentId, AuthorAccountId = comment.AuthorAccountId,
        Message = comment.Message, CreatedAt = RevisionTimestamp(comment.CreatedAt)
    };

    private Task<IDbContextTransaction> BeginDocumentRequestTransactionAsync(CancellationToken cancellationToken) => _db.Database.IsRelational()
        ? _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
        : _db.Database.BeginTransactionAsync(cancellationToken);

    private static DateTime RevisionTimestamp(DateTime timestamp) => timestamp.Kind == DateTimeKind.Local
        ? timestamp.ToUniversalTime() : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

    private static bool DocumentRequestWriteConflict(Exception error) => error is DbUpdateConcurrencyException ||
        error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
        error is PostgresException { SqlState: "23505" or "40001" or "40P01" };
}
