using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public Task<Response.DocumentUploadResponse> UploadDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, CancellationToken cancellationToken = default) =>
        UploadDocumentCoreAsync(accountId, dossierId, requirementId, request, false, cancellationToken);

    public Task<Response.DocumentUploadResponse> SubmitDocumentVersionAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, CancellationToken cancellationToken = default) =>
        UploadDocumentCoreAsync(accountId, dossierId, requirementId, request, true, cancellationToken);

    private async Task<Response.DocumentUploadResponse> UploadDocumentCoreAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, bool existing, CancellationToken cancellationToken)
    {
        var (dossier, requirement) = await UploadContextAsync(accountId, dossierId, requirementId, false, cancellationToken, existing);
        ComplianceDocument? document = null;
        DocumentVersion? previous = null;
        if (existing) (document, previous) = await ExistingDocumentAsync(requirementId, false, cancellationToken);
        var expectedVersionId = previous?.DocumentVersionId;
        var expectedFileUrl = previous?.FileUrl;
        if (request.Files is null || request.Files.Count is < 1 or > 10)
            throw new BadRequestException("Submit between 1 and 10 document files in one version.");
        if (request.Files.Any(file => file is null || string.IsNullOrWhiteSpace(file.PublicId)) ||
            request.Files.Select(file => file.PublicId).Distinct(StringComparer.Ordinal).Count() != request.Files.Count)
            throw new BadRequestException("Provide distinct non-empty publicIds for all document files.", "INVALID_PUBLIC_ID");
        ValidateDocumentMetadata(ResolveDocumentMetadata(request, document), dossier, requirement);
        var storageId = Guid.NewGuid();
        var uploaded = new List<StoredDocumentFile>();
        var readUrls = new List<SignedDocumentUrl>();
        var supersededUrls = new List<string>();
        var commitAttempted = false;
        var committed = false;
        try
        {
            foreach (var reference in request.Files)
            {
                var asset = await _mediaService.ConfirmUploadAsync(accountId, reference.PublicId, cancellationToken);
                DocumentStorageService.Service.ValidateFileMetadata(asset.FileName, asset.MimeType, asset.SizeBytes);
                var content = await _mediaService.DownloadPreviewAsync(asset.FileUrl, cancellationToken);
                if (content.Content.Length != asset.SizeBytes || content.ContentType != asset.MimeType)
                    throw new BadRequestException("Uploaded file content does not match its metadata.", "INVALID_FILE_TYPE");
                await _documentStorage.ValidateContentAsync(content, cancellationToken);
                uploaded.Add(new StoredDocumentFile(asset.FileUrl, asset.FileName, asset.MimeType, asset.SizeBytes));
            }
            foreach (var file in uploaded)
                readUrls.Add(await _mediaService.CreateReadUrlAsync(file.StorageUrl, cancellationToken));
            DocumentVersion version;
            bool newVersion;
            await using (var transaction = await _db.Database.BeginSerializableAsync(cancellationToken))
            {
                try
                {
                    (dossier, requirement) = await UploadContextAsync(accountId, dossierId, requirementId, true, cancellationToken, existing);
                    if (existing) (document, previous) = await ExistingDocumentAsync(requirementId, true, cancellationToken);
                    if (existing && (previous!.DocumentVersionId != expectedVersionId || previous.FileUrl != expectedFileUrl))
                        throw new ConflictException("The document changed during upload. Reload before submitting.", "DOCUMENT_UPLOAD_CONFLICT");
                    var metadata = ResolveDocumentMetadata(request, document);
                    ValidateDocumentMetadata(metadata, dossier, requirement);
                    newVersion = previous is null || previous.DocumentReviews.Count != 0 || document?.Status == ComplianceDocumentStatus.REJECTED;
                    var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                    if (document is null)
                    {
                        document = new ComplianceDocument
                        {
                            ComplianceDocumentId = Guid.NewGuid(), DossierRequirementId = requirementId,
                            CreatedByAccountId = accountId, CreatedAt = now, DocumentCode = $"CDOC-{Guid.NewGuid():N}",
                            DocumentName = requirement.ComplianceRequirement.RequirementName
                        };
                        _db.ComplianceDocuments.Add(document);
                    }
                    ApplyDocumentMetadata(document, metadata, accountId, now);
                    document.Status = ComplianceDocumentStatus.PROVIDED;
                    if (newVersion)
                    {
                        version = new DocumentVersion
                        {
                            DocumentVersionId = storageId, ComplianceDocument = document, UploadedByAccountId = accountId,
                            VersionNo = previous is null ? 1 : checked(previous.VersionNo + 1), CreatedAt = now
                        };
                        _db.DocumentVersions.Add(version);
                    }
                    else
                    {
                        version = previous!;
                        supersededUrls.Add(version.FileUrl);
                        supersededUrls.AddRange(version.DocumentAttachments.OrderBy(item => item.SortOrder).Select(item => item.FileUrl));
                        _db.DocumentAttachments.RemoveRange(version.DocumentAttachments);
                        version.DocumentAttachments.Clear();
                    }
                    version.FileName = uploaded[0].FileName;
                    version.FileUrl = uploaded[0].StorageUrl;
                    version.MimeType = uploaded[0].MimeType;
                    version.FileSizeBytes = uploaded[0].SizeBytes;
                    version.UploadNote = request.UploadNote?.Trim();
                    version.UploadedByAccountId = accountId;
                    version.UploadedAt = now;
                    for (var index = 1; index < uploaded.Count; index++)
                        _db.DocumentAttachments.Add(new DocumentAttachment
                        {
                            DocumentAttachmentId = Guid.NewGuid(), DocumentVersion = version,
                            FileName = uploaded[index].FileName, FileUrl = uploaded[index].StorageUrl,
                            MimeType = uploaded[index].MimeType, FileSizeBytes = uploaded[index].SizeBytes, SortOrder = index, CreatedAt = now
                        });
                    requirement.Status = DossierRequirementStatus.PROVIDED;
                    requirement.SatisfiedAt = null;
                    requirement.UpdatedAt = now;
                    dossier.UpdatedAt = now;
                    _db.AuditDossiers.Add(new AuditDossier
                    {
                        AuditDossierId = Guid.NewGuid(), ComplianceDossierId = dossierId, TargetEntity = nameof(DocumentVersion),
                        TargetId = version.DocumentVersionId, ActorAccountId = accountId,
                        ActionType = newVersion ? "DOCUMENT_VERSION_SUBMITTED" : "DOCUMENT_VERSION_UPDATED",
                        Summary = $"DocumentVersion v{version.VersionNo} {(newVersion ? "submitted" : "updated")}.",
                        Visibility = AuditVisibility.Public, CreatedAt = now,
                        Details = JsonSerializer.SerializeToDocument(new { document.ComplianceDocumentId, requirementId, version.VersionNo, FileCount = uploaded.Count, version.UploadNote })
                    });
                    var recipients = await _db.Employees.Active()
                        .Where(employee => employee.Role == EmployeeRole.TRANSPORT_SPECIALIST &&
                            employee.EmployeeTransportPlans.Any(assignment => assignment.TransportPlan.TransportId == dossier.TransportId))
                        .Select(employee => employee.AccountId).Distinct().ToListAsync(cancellationToken);
                    foreach (var recipient in recipients)
                        AddNotification(recipient, EmployeeRole.TRANSPORT_SPECIALIST, dossier, "Document submitted for review",
                            $"{requirement.ComplianceRequirement.RequirementName} was uploaded for dossier {dossier.DossierCode} and is waiting for your review.", now);
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
                    }
                    catch (Exception rollbackError)
                    {
                        _logger.LogError(rollbackError, "Document upload rollback could not be confirmed for {StorageId}", storageId);
                    }
                    throw;
                }
            }
            await _notificationService.FlushAsync(cancellationToken);
            await CleanupDocumentFilesAsync(supersededUrls, storageId);
            return new Response.DocumentUploadResponse
            {
                DossierRequirementId = requirementId, ComplianceDocumentId = document!.ComplianceDocumentId,
                DocumentVersionId = version.DocumentVersionId, VersionNo = version.VersionNo, IsNewVersion = newVersion, Status = requirement.Status.ToString(),
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
            if (!committed && commitAttempted)
                _logger.LogError(error, "Document commit outcome is unknown for {StorageId}; files retained to prevent data loss", storageId);
            if (PostgresErrors.IsWriteConflict(error))
                throw new ConflictException("ComplianceDocument already exists or the dossier changed. Reload before uploading.", "DOCUMENT_UPLOAD_CONFLICT");
            throw;
        }
    }

    private async Task CleanupDocumentFilesAsync(IEnumerable<string> urls, Guid storageId)
    {
        foreach (var url in urls.Where(url => url.StartsWith("s3://", StringComparison.Ordinal)))
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _documentStorage.DeleteAsync(url, cleanup.Token);
            }
            catch (Exception cleanupError)
            {
                _logger.LogError(cleanupError, "Failed to remove unused document file for {StorageId}", storageId);
            }
        }
    }

    private async Task<(ComplianceDossier Dossier, DossierRequirement Requirement)> UploadContextAsync(Guid accountId,
        Guid dossierId, Guid requirementId, bool tracking, CancellationToken cancellationToken, bool existing = false, bool readOnly = false)
    {
        var customerId = await _db.Customers.AsNoTracking().Where(item => item.AccountId == accountId && !item.IsDeleted &&
            item.Account.AccountType == AccountType.CUSTOMER && !item.Account.IsDeleted && item.Account.Status == AccountStatus.ACTIVE)
            .Select(item => (Guid?)item.CustomerId).SingleOrDefaultAsync(cancellationToken);
        var specialist = readOnly && await _db.Employees.AsNoTracking().Active().AnyAsync(item => item.AccountId == accountId && item.Account.AccountType == AccountType.EMPLOYEE && (item.Role == EmployeeRole.TRANSPORT_SPECIALIST || item.Role == EmployeeRole.LOGISTICS_MANAGER), cancellationToken);
        if (!customerId.HasValue && !specialist)
            throw new ForbiddenException("Only an active Customer can upload ComplianceDocuments.");
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
        if (!specialist && dossier.Transport.Booking.CustomerId != customerId)
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
        if (readOnly) return (dossier, requirement);
        if (existing && requirement.Status == DossierRequirementStatus.SATISFIED)
            throw SatisfiedDocument();
        if (existing && dossier.Status is ComplianceDossierStatus.SUBMITTED or ComplianceDossierStatus.PENDING)
            throw new ConflictException("Cannot submit new DocumentVersions while ComplianceDossier is undergoing official authority review.");
        if (dossier.Status is not (ComplianceDossierStatus.IN_PREPARATION or ComplianceDossierStatus.REJECTED))
            throw new ConflictException("ComplianceDocuments can only be uploaded while the dossier is IN_PREPARATION or REJECTED.");
        if (existing ? requirement.Status is not (DossierRequirementStatus.PROVIDED or DossierRequirementStatus.REJECTED or DossierRequirementStatus.REQUESTED)
            : requirement.Status is not (DossierRequirementStatus.PENDING or DossierRequirementStatus.REQUESTED or DossierRequirementStatus.REJECTED))
            throw new ConflictException(existing
                ? "DocumentVersions can only be submitted for PROVIDED, REJECTED or REQUESTED requirements."
                : "ComplianceDocuments can only be uploaded for PENDING, REQUESTED or REJECTED requirements.");
        if (!existing && requirement.Status == DossierRequirementStatus.REQUESTED &&
            !await _db.DocumentRequests.AnyAsync(item => item.DossierRequirementId == requirementId &&
                item.RequestType == DocumentRequestType.MISSING_DOCUMENT && item.Status == DocumentRequestStatus.OPEN, cancellationToken))
            throw new ConflictException("An OPEN missing-document request is required before uploading a REQUESTED requirement.");
        if (!existing && await _db.ComplianceDocuments.AnyAsync(item => item.DossierRequirementId == requirementId, cancellationToken))
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
