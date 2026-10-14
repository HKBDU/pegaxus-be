using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.DocumentMetadataResponse> UpdateDocumentMetadataAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentMetadataRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginSerializableAsync(cancellationToken);
        try
        {
            var (dossier, requirement) = await UploadContextAsync(accountId, dossierId, requirementId, true, cancellationToken, true);
            var (document, _) = await ExistingDocumentAsync(requirementId, true, cancellationToken);
            var metadata = ResolveDocumentMetadata(request, document);
            ValidateDocumentMetadata(metadata, dossier, requirement);
            var oldMetadata = new { document.DocumentNumber, document.IssuingAuthority, document.IssuedDate, document.ExpiryDate };
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            ApplyDocumentMetadata(document, metadata, accountId, now);
            dossier.UpdatedAt = now;
            _db.AuditDossiers.Add(new AuditDossier
            {
                AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, TargetEntity = nameof(ComplianceDocument),
                TargetId = document.ComplianceDocumentId, ActorAccountId = accountId, ActionType = "DOCUMENT_METADATA_UPDATED",
                Summary = "ComplianceDocument metadata updated without changing version history.", Visibility = AuditVisibility.Public, CreatedAt = now,
                Details = JsonSerializer.SerializeToDocument(new { Before = oldMetadata, After = new { document.DocumentNumber, document.IssuingAuthority, document.IssuedDate, document.ExpiryDate } })
            });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new Response.DocumentMetadataResponse
            {
                ComplianceDocumentId = document.ComplianceDocumentId, DocumentNumber = document.DocumentNumber,
                IssuingAuthority = document.IssuingAuthority, IssuedDate = document.IssuedDate, ExpiryDate = document.ExpiryDate,
                Status = document.Status.ToString()
            };
        }
        catch (Exception error) when (PostgresErrors.IsWriteConflict(error))
        {
            throw new ConflictException("The document changed. Reload before updating metadata.", "DOCUMENT_UPDATE_CONFLICT");
        }
    }

    public async Task<IReadOnlyList<Response.DocumentVersionResponse>> GetDocumentVersionsAsync(Guid accountId, Guid dossierId, Guid requirementId,
        CancellationToken cancellationToken = default)
    {
        await UploadContextAsync(accountId, dossierId, requirementId, false, cancellationToken, true, true);
        var documentId = await _db.ComplianceDocuments.AsNoTracking().Where(item => item.DossierRequirementId == requirementId)
            .Select(item => (Guid?)item.ComplianceDocumentId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("ComplianceDocument not found.", "DOCUMENT_NOT_FOUND");
        var versions = await _db.DocumentVersions.AsNoTracking().Include(item => item.DocumentAttachments).Include(item => item.DocumentReviews)
            .Where(item => item.ComplianceDocumentId == documentId).OrderByDescending(item => item.VersionNo).ToListAsync(cancellationToken);
        var result = new List<Response.DocumentVersionResponse>();
        foreach (var version in versions)
        {
            var files = new List<Response.UploadedDocumentFileResponse>();
            files.Add(await ReadDocumentFileAsync(null, version.FileName, version.FileUrl, version.MimeType, version.FileSizeBytes, cancellationToken));
            foreach (var file in version.DocumentAttachments.OrderBy(item => item.SortOrder))
                files.Add(await ReadDocumentFileAsync(file.DocumentAttachmentId, file.FileName, file.FileUrl, file.MimeType, file.FileSizeBytes, cancellationToken));
            result.Add(new Response.DocumentVersionResponse
            {
                DocumentVersionId = version.DocumentVersionId, VersionNo = version.VersionNo, UploadedByAccountId = version.UploadedByAccountId,
                UploadedAt = version.UploadedAt, UploadNote = version.UploadNote, Files = files,
                Reviews = version.DocumentReviews.OrderBy(item => item.ReviewedAt).Select(item => new Response.DocumentReviewResponse
                {
                    DocumentReviewId = item.DocumentReviewId, ReviewedByAccountId = item.ReviewedByAccountId, Result = item.Result.ToString(),
                    ReviewComment = item.ReviewComment, ReviewedAt = item.ReviewedAt
                }).ToList()
            });
        }
        return result;
    }

    private async Task<Response.UploadedDocumentFileResponse> ReadDocumentFileAsync(Guid? attachmentId, string name, string url,
        string? mimeType, long? size, CancellationToken cancellationToken)
    {
        var signed = url.StartsWith("s3://", StringComparison.Ordinal)
            ? await _documentStorage.CreateReadUrlAsync(url, cancellationToken)
            : await _mediaService.CreateReadUrlAsync(url, cancellationToken);
        return new Response.UploadedDocumentFileResponse
        {
            DocumentAttachmentId = attachmentId, FileName = name, MimeType = mimeType ?? string.Empty, FileSizeBytes = size ?? 0,
            ReadUrl = signed.Url, ReadUrlExpiresAt = signed.ExpiresAt
        };
    }

    private async Task<(ComplianceDocument Document, DocumentVersion Latest)> ExistingDocumentAsync(Guid requirementId, bool tracking,
        CancellationToken cancellationToken)
    {
        // Updating the key to itself holds a row lock until the transaction ends.
        if (tracking && _db.Database.IsRelational())
            await _db.LockRowsAsync(_db.ComplianceDocuments.Where(item => item.DossierRequirementId == requirementId), item => item.ComplianceDocumentId, cancellationToken);
        var documents = _db.ComplianceDocuments.AsQueryable();
        var document = await (tracking ? documents.AsTracking() : documents.AsNoTracking())
            .SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDocument not found.", "DOCUMENT_NOT_FOUND");
        if (tracking) await _db.Entry(document).ReloadAsync(cancellationToken);
        if (tracking && _db.Database.IsRelational())
            await _db.LockRowsAsync(_db.DocumentVersions.Where(item => item.ComplianceDocumentId == document.ComplianceDocumentId)
                .OrderByDescending(item => item.VersionNo).Take(1), item => item.DocumentVersionId, cancellationToken);
        var versions = _db.DocumentVersions.AsQueryable();
        var latest = await (tracking ? versions.AsTracking() : versions.AsNoTracking())
            .Include(item => item.DocumentAttachments).Include(item => item.DocumentReviews)
            .Where(item => item.ComplianceDocumentId == document.ComplianceDocumentId).OrderByDescending(item => item.VersionNo)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("ComplianceDocument has no existing DocumentVersion.");
        if (tracking) await _db.Entry(latest).ReloadAsync(cancellationToken);
        if (document.Status == ComplianceDocumentStatus.APPROVED ||
            (await LatestReviewAsync(latest.DocumentVersionId, cancellationToken))?.Result == DocumentReviewResult.APPROVED)
            throw SatisfiedDocument();
        if (document.Status is not (ComplianceDocumentStatus.PROVIDED or ComplianceDocumentStatus.REJECTED))
            throw new ConflictException("Only PROVIDED or REJECTED ComplianceDocuments can be edited.");
        return (document, latest);
    }

    private static Request.DocumentUploadRequest ResolveDocumentMetadata(Request.DocumentMetadataRequest request, ComplianceDocument? document) => new()
    {
        DocumentNumber = request.DocumentNumber ?? document?.DocumentNumber,
        IssuingAuthority = request.IssuingAuthority ?? document?.IssuingAuthority,
        IssuedDate = request.IssuedDate ?? document?.IssuedDate,
        ExpiryDate = request.ExpiryDate ?? document?.ExpiryDate,
        UploadNote = (request as Request.DocumentUploadRequest)?.UploadNote
    };

    private static void ApplyDocumentMetadata(ComplianceDocument document, Request.DocumentMetadataRequest metadata, Guid accountId, DateTime now)
    {
        document.DocumentNumber = metadata.DocumentNumber!.Trim();
        document.IssuingAuthority = metadata.IssuingAuthority!.Trim();
        document.IssuedDate = metadata.IssuedDate;
        document.ExpiryDate = metadata.ExpiryDate;
        document.UpdatedByAccountId = accountId;
        document.UpdatedAt = now;
    }

    private static ConflictException SatisfiedDocument() => new(
        "This ComplianceDocument has already been satisfied and locked by Transport Specialist. Please contact support to unlock.");
}
