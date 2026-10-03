using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.DocumentUploadResponse> UploadDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, CancellationToken cancellationToken = default)
    {
        var (dossier, requirement) = await UploadContextAsync(accountId, dossierId, requirementId, false, cancellationToken);
        if (request.Files is null || request.Files.Count is < 1 or > 10)
            throw new BadRequestException("Submit between 1 and 10 document files in one version.");
        foreach (var file in request.Files) DocumentStorageService.Service.ValidateFileMetadata(file);
        ValidateDocumentMetadata(request, dossier, requirement);
        var versionId = Guid.NewGuid();
        var uploaded = new List<StoredDocumentFile>();
        var readUrls = new List<SignedDocumentUrl>();
        var databaseWriteStarted = false;
        var commitAttempted = false;
        var committed = false;
        var rollbackConfirmed = false;
        try
        {
            for (var index = 0; index < request.Files.Count; index++)
                uploaded.Add(await _documentStorage.UploadAsync(versionId, index, request.Files[index], cancellationToken));
            foreach (var file in uploaded)
                readUrls.Add(await _documentStorage.CreateReadUrlAsync(file.StorageUrl, cancellationToken));
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            var document = new ComplianceDocument
            {
                ComplianceDocumentId = Guid.NewGuid(), DossierRequirementId = requirementId,
                CreatedByAccountId = accountId, UpdatedByAccountId = accountId,
                DocumentCode = $"CDOC-{Guid.NewGuid():N}", DocumentName = requirement.ComplianceRequirement.RequirementName,
                DocumentNumber = request.DocumentNumber.Trim(), IssuingAuthority = request.IssuingAuthority.Trim(),
                IssuedDate = request.IssuedDate, ExpiryDate = request.ExpiryDate, Status = ComplianceDocumentStatus.PROVIDED,
                CreatedAt = now, UpdatedAt = now
            };
            var version = new DocumentVersion
            {
                DocumentVersionId = versionId, ComplianceDocument = document, UploadedByAccountId = accountId, VersionNo = 1,
                FileName = uploaded[0].FileName, FileUrl = uploaded[0].StorageUrl, MimeType = uploaded[0].MimeType,
                FileSizeBytes = uploaded[0].SizeBytes, UploadNote = request.UploadNote?.Trim(), UploadedAt = now, CreatedAt = now
            };
            document.DocumentVersions.Add(version);
            for (var index = 1; index < uploaded.Count; index++)
                version.DocumentAttachments.Add(new DocumentAttachment
                {
                    DocumentAttachmentId = Guid.NewGuid(), FileName = uploaded[index].FileName, FileUrl = uploaded[index].StorageUrl,
                    MimeType = uploaded[index].MimeType, FileSizeBytes = uploaded[index].SizeBytes, SortOrder = index, CreatedAt = now
                });
            await using (var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : await _db.Database.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    (dossier, requirement) = await UploadContextAsync(accountId, dossierId, requirementId, true, cancellationToken);
                    ValidateDocumentMetadata(request, dossier, requirement);
                    document.DocumentName = requirement.ComplianceRequirement.RequirementName;
                    databaseWriteStarted = true;
                    _db.ComplianceDocuments.Add(document);
                    requirement.Status = DossierRequirementStatus.PROVIDED;
                    requirement.SatisfiedAt = null;
                    requirement.UpdatedAt = now;
                    dossier.UpdatedAt = now;
                    _db.AuditDossiers.Add(new AuditDossier
                    {
                        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, TargetEntity = nameof(DocumentVersion),
                        TargetId = versionId, ActorAccountId = accountId, ActionType = "DOCUMENT_VERSION_SUBMITTED",
                        Summary = "ComplianceDocument uploaded (Version v1).", Visibility = AuditVisibility.Public, CreatedAt = now,
                        Details = JsonSerializer.SerializeToDocument(new { document.ComplianceDocumentId, requirementId, VersionNo = 1, FileCount = uploaded.Count })
                    });
                    await _db.SaveChangesAsync(cancellationToken);
                    commitAttempted = true;
                    await transaction.CommitAsync(cancellationToken);
                    committed = true;
                }
                catch
                {
                    try
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        rollbackConfirmed = true;
                    }
                    catch (Exception rollbackError)
                    {
                        _logger.LogError(rollbackError, "Document upload rollback could not be confirmed for {VersionId}", versionId);
                    }
                    throw;
                }
            }
            return new Response.DocumentUploadResponse
            {
                DossierRequirementId = requirementId, ComplianceDocumentId = document.ComplianceDocumentId,
                DocumentVersionId = versionId, VersionNo = 1, Status = requirement.Status.ToString(),
                Files = uploaded.Select((file, index) => new Response.UploadedDocumentFileResponse
                {
                    DocumentAttachmentId = index == 0 ? null : version.DocumentAttachments.Single(item => item.SortOrder == index).DocumentAttachmentId,
                    FileName = file.FileName, MimeType = file.MimeType, FileSizeBytes = file.SizeBytes,
                    ReadUrl = readUrls[index].Url, ReadUrlExpiresAt = readUrls[index].ExpiresAt
                }).ToList()
            };
        }
        catch (Exception error)
        {
            if (!committed && (!databaseWriteStarted || rollbackConfirmed && !commitAttempted))
            {
                foreach (var file in uploaded)
                {
                    try
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await _documentStorage.DeleteAsync(file.StorageUrl, cleanup.Token);
                    }
                    catch (Exception cleanupError)
                    {
                        _logger.LogError(cleanupError, "Failed to remove uncommitted document file for {VersionId}", versionId);
                    }
                }
            }
            else if (!committed)
                _logger.LogError(error, "Document commit outcome is unknown for {VersionId}; files retained to prevent data loss", versionId);
            if (error is DbUpdateConcurrencyException ||
                error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } } ||
                error is PostgresException { SqlState: "23505" or "40001" or "40P01" })
                throw new ConflictException("ComplianceDocument already exists or the dossier changed. Reload before uploading.", "DOCUMENT_UPLOAD_CONFLICT");
            throw;
        }
    }

    private async Task<(ComplianceDossier Dossier, DossierRequirement Requirement)> UploadContextAsync(Guid accountId,
        Guid dossierId, Guid requirementId, bool tracking, CancellationToken cancellationToken)
    {
        var customerId = await _db.Customers.AsNoTracking().Where(item => item.AccountId == accountId && !item.IsDeleted &&
            item.Account.AccountType == AccountType.CUSTOMER && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
            .Select(item => (Guid?)item.CustomerId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("Only an active Customer can upload ComplianceDocuments.");
        var dossiers = _db.ComplianceDossiers.Include(item => item.Transport).ThenInclude(item => item.Booking);
        var dossier = await (tracking ? dossiers.AsTracking() : dossiers.AsNoTracking())
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        if (tracking)
        {
            await _db.Entry(dossier).ReloadAsync(cancellationToken);
            if (_db.Entry(dossier).State == EntityState.Detached)
                throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
            await _db.Entry(dossier.Transport.Booking).ReloadAsync(cancellationToken);
        }
        if (dossier.Transport.Booking.CustomerId != customerId)
            throw new ForbiddenException("You do not have permission to upload to this ComplianceDossier.");
        var requirements = _db.DossierRequirements.Include(item => item.ComplianceRequirement);
        var requirement = await (tracking ? requirements.AsTracking() : requirements.AsNoTracking())
            .SingleOrDefaultAsync(item => item.DossierRequirementId == requirementId && item.ComplianceDossierId == dossierId &&
                !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        if (tracking)
        {
            await _db.Entry(requirement).ReloadAsync(cancellationToken);
            await _db.Entry(requirement.ComplianceRequirement).ReloadAsync(cancellationToken);
        }
        if (requirement.IsDeleted || _db.Entry(requirement).State == EntityState.Detached && tracking)
            throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        if (dossier.Status is not (ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.REJECTED))
            throw new ConflictException("ComplianceDocuments can only be uploaded while the dossier is IN_PREPARATION or REJECTED.");
        if (requirement.Status is not (DossierRequirementStatus.PENDING or DossierRequirementStatus.REJECTED))
            throw new ConflictException("ComplianceDocuments can only be uploaded for PENDING or REJECTED requirements.");
        if (await _db.ComplianceDocuments.AnyAsync(item => item.DossierRequirementId == requirementId, cancellationToken))
            throw new ConflictException("ComplianceDocument already exists. Submit a new DocumentVersion instead.", "DOCUMENT_ALREADY_EXISTS");
        return (dossier, requirement);
    }

    private static void ValidateDocumentMetadata(Request.DocumentUploadRequest request, ComplianceDossier dossier, DossierRequirement requirement)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentNumber) || request.DocumentNumber.Length > 150)
            throw new BadRequestException("DocumentNumber is required and must not exceed 150 characters.");
        if (string.IsNullOrWhiteSpace(request.IssuingAuthority) || request.IssuingAuthority.Length > 200)
            throw new BadRequestException("IssuingAuthority is required and must not exceed 200 characters.");
        if (!request.IssuedDate.HasValue || request.IssuedDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BadRequestException("IssuedDate is required and must not be in the future.");
        if (request.UploadNote?.Length > 2000)
            throw new BadRequestException("UploadNote must not exceed 2000 characters.");
        if ((requirement.ValidUntil.HasValue || requirement.ComplianceRequirement.ValidityRequired) && !request.ExpiryDate.HasValue)
            throw new BadRequestException("ExpiryDate is required for this DossierRequirement.");
        if (request.ExpiryDate.HasValue && request.ExpiryDate <= dossier.Transport.Booking.RequestedDepartureDate)
            throw new BadRequestException("The document expiry date must be after the planned transport departure date.");
        if (request.ExpiryDate < request.IssuedDate)
            throw new BadRequestException("ExpiryDate must not be before IssuedDate.");
        if (request.ExpiryDate.HasValue && requirement.ValidUntil.HasValue && request.ExpiryDate < requirement.ValidUntil)
            throw new BadRequestException("The document expiry date must satisfy the DossierRequirement valid_until date.");
    }
}
