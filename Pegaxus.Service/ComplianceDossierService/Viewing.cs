using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.ComplianceDossierService;

public sealed partial class Service
{
    public async Task<Response.OverviewResponse> GetByBookingAsync(Guid accountId, Guid bookingId,
        Request.ViewRequest request, CancellationToken cancellationToken = default)
    {
        var viewer = await RequireViewerAsync(accountId, cancellationToken);
        var booking = await _db.Bookings.AsNoTracking().SingleOrDefaultAsync(item => item.BookingId == bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking not found.", "BOOKING_NOT_FOUND");
        CheckOwnership(viewer.CustomerId, booking.CustomerId);
        ValidateViewFilter(request);
        var dossier = await _db.ComplianceDossiers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Transport.BookingId == bookingId, cancellationToken);
        if (dossier is null)
            return new Response.OverviewResponse
            {
                BookingId = bookingId, BookingCode = booking.BookingCode,
                BookingSummary = await BuildBookingSummaryAsync(bookingId, cancellationToken),
                Message = "ComplianceDossier has not been initialized. Waiting for transport booking approval."
            };
        return await BuildOverviewAsync(dossier, booking, viewer.Role, request, cancellationToken);
    }

    public async Task<Response.OverviewResponse> GetDossierAsync(Guid accountId, Guid dossierId,
        Request.ViewRequest request, CancellationToken cancellationToken = default)
    {
        var (dossier, booking, role) = await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        return await BuildOverviewAsync(dossier, booking, role, request, cancellationToken);
    }

    public async Task<Response.RequirementDetailResponse> GetRequirementAsync(Guid accountId, Guid dossierId,
        Guid requirementId, CancellationToken cancellationToken = default)
    {
        var (_, booking, role) = await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        var requirement = await _db.DossierRequirements.AsNoTracking().AsSplitQuery()
            .Include(item => item.ComplianceRequirement).Include(item => item.Horse)
            .Include(item => item.DocumentRequests)
            .Include(item => item.ComplianceDocument!).ThenInclude(item => item.DocumentVersions).ThenInclude(item => item.DocumentReviews)
            .Include(item => item.ComplianceDocument!).ThenInclude(item => item.DocumentVersions).ThenInclude(item => item.DocumentAttachments)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId && item.DossierRequirementId == requirementId &&
                !item.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("DossierRequirement not found.", "REQUIREMENT_NOT_FOUND");
        var document = requirement.ComplianceDocument;
        var previewBase = $"/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/versions";
        return new Response.RequirementDetailResponse
        {
            Requirement = MapRequirement(requirement, booking.RequestedDepartureDate),
            ValidUntil = requirement.ValidUntil, SatisfiedAt = requirement.SatisfiedAt, WaivedReason = requirement.WaivedReason,
            Actions = ViewActions(role),
            DocumentRequests = requirement.DocumentRequests.OrderByDescending(item => item.RequestedAt)
                .ThenByDescending(item => item.DocumentRequestId).Select(MapDocumentRequest).ToList(),
            Document = document is null ? null : new Response.DocumentResponse
            {
                ComplianceDocumentId = document.ComplianceDocumentId, DocumentCode = document.DocumentCode,
                DocumentName = document.DocumentName, Status = document.Status.ToString(), DocumentNumber = document.DocumentNumber,
                IssuingAuthority = document.IssuingAuthority, IssuedDate = document.IssuedDate, ExpiryDate = document.ExpiryDate,
                Note = document.Note, UpdatedAt = DateTime.SpecifyKind(document.UpdatedAt, DateTimeKind.Utc),
                Versions = document.DocumentVersions.OrderByDescending(item => item.VersionNo).Select(version => new Response.VersionResponse
                {
                    DocumentVersionId = version.DocumentVersionId, VersionNo = version.VersionNo, FileName = version.FileName,
                    PreviewUrl = $"{previewBase}/{version.DocumentVersionId}/preview", MimeType = version.MimeType,
                    FileSizeBytes = version.FileSizeBytes, UploadNote = version.UploadNote, UploadedAt = DateTime.SpecifyKind(version.UploadedAt, DateTimeKind.Utc),
                    Attachments = version.DocumentAttachments.OrderBy(item => item.SortOrder).ThenBy(item => item.DocumentAttachmentId)
                        .Select(attachment => new Response.AttachmentResponse
                        {
                            DocumentAttachmentId = attachment.DocumentAttachmentId, FileName = attachment.FileName,
                            PreviewUrl = $"{previewBase}/{version.DocumentVersionId}/attachments/{attachment.DocumentAttachmentId}/preview",
                            MimeType = attachment.MimeType, FileSizeBytes = attachment.FileSizeBytes, SortOrder = attachment.SortOrder
                        }).ToList(),
                    Reviews = version.DocumentReviews.OrderByDescending(item => item.ReviewedAt).ThenBy(item => item.DocumentReviewId)
                        .Select(review => new Response.ReviewResponse
                        {
                            DocumentReviewId = review.DocumentReviewId, ReviewedByAccountId = review.ReviewedByAccountId,
                            Result = review.Result.ToString(), ReviewComment = review.ReviewComment, ReviewedAt = DateTime.SpecifyKind(review.ReviewedAt, DateTimeKind.Utc)
                        }).ToList()
                }).ToList()
            }
        };
    }

    public async Task<PreviewFile> GetDocumentPreviewAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Guid versionId, Guid? attachmentId, CancellationToken cancellationToken = default)
    {
        await ViewableDossierAsync(accountId, dossierId, cancellationToken);
        var versions = _db.DocumentVersions.AsNoTracking().Where(item => item.DocumentVersionId == versionId &&
            item.ComplianceDocument.DossierRequirementId == requirementId &&
            item.ComplianceDocument.DossierRequirement.ComplianceDossierId == dossierId &&
            !item.ComplianceDocument.DossierRequirement.IsDeleted);
        var fileUrl = attachmentId.HasValue
            ? await versions.SelectMany(item => item.DocumentAttachments)
                .Where(item => item.DocumentAttachmentId == attachmentId).Select(item => item.FileUrl).SingleOrDefaultAsync(cancellationToken)
            : await versions.Select(item => item.FileUrl).SingleOrDefaultAsync(cancellationToken);
        if (fileUrl is null)
            throw new NotFoundException("Document file not found.", "DOCUMENT_FILE_NOT_FOUND");
        return fileUrl.StartsWith("s3://", StringComparison.Ordinal)
            ? await _documentStorage.DownloadPreviewAsync(fileUrl, cancellationToken)
            : await _mediaService.DownloadPreviewAsync(fileUrl, cancellationToken);
    }

    private async Task<(Guid? CustomerId, EmployeeRole? Role)> RequireViewerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var viewer = await _db.Accounts.AsNoTracking().Where(item => item.AccountId == accountId && !item.IsDeleted &&
            item.Status == AccountStatus.ACTIVE &&
            (item.AccountType == AccountType.CUSTOMER && item.CustomerAccount != null && !item.CustomerAccount.IsDeleted ||
             item.AccountType == AccountType.EMPLOYEE && item.EmployeeAccount != null && !item.EmployeeAccount.IsDeleted &&
             item.EmployeeAccount.IsActive && (item.EmployeeAccount.Role == EmployeeRole.TRANSPORT_SPECIALIST ||
                 item.EmployeeAccount.Role == EmployeeRole.LOGISTICS_MANAGER)))
            .Select(item => new
            {
                CustomerId = item.AccountType == AccountType.CUSTOMER ? (Guid?)item.CustomerAccount!.CustomerId : null,
                Role = item.AccountType == AccountType.EMPLOYEE ? (EmployeeRole?)item.EmployeeAccount!.Role : null
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ForbiddenException("Only Customers, Transport Specialists and Logistics Managers can view ComplianceDossiers.");
        return (viewer.CustomerId, viewer.Role);
    }

    private static void CheckOwnership(Guid? customerId, Guid bookingCustomerId)
    {
        if (customerId.HasValue && customerId != bookingCustomerId)
            throw new ForbiddenException("You do not have permission to view this ComplianceDossier.");
    }

    private async Task<(ComplianceDossier Dossier, Booking Booking, EmployeeRole? Role)> ViewableDossierAsync(
        Guid accountId, Guid dossierId, CancellationToken cancellationToken)
    {
        var viewer = await RequireViewerAsync(accountId, cancellationToken);
        var dossier = await _db.ComplianceDossiers.AsNoTracking().Include(item => item.Transport).ThenInclude(item => item.Booking)
            .SingleOrDefaultAsync(item => item.ComplianceDossierId == dossierId, cancellationToken)
            ?? throw new NotFoundException("ComplianceDossier not found.", "DOSSIER_NOT_FOUND");
        CheckOwnership(viewer.CustomerId, dossier.Transport.Booking.CustomerId);
        return (dossier, dossier.Transport.Booking, viewer.Role);
    }

    private async Task<Response.OverviewResponse> BuildOverviewAsync(ComplianceDossier dossier, Booking booking,
        EmployeeRole? role, Request.ViewRequest request, CancellationToken cancellationToken)
    {
        var status = ValidateViewFilter(request);
        var requirements = await _db.DossierRequirements.AsNoTracking()
            .Where(item => item.ComplianceDossierId == dossier.ComplianceDossierId && !item.IsDeleted)
            .OrderBy(item => item.ComplianceRequirement.RequirementName).ThenBy(item => item.DossierRequirementId)
            .Select(item => new Response.RequirementResponse
            {
                DossierRequirementId = item.DossierRequirementId, ComplianceRequirementId = item.ComplianceRequirementId,
                RequirementCode = item.ComplianceRequirement.RequirementCode, RequirementName = item.ComplianceRequirement.RequirementName,
                HorseId = item.HorseId, HorseName = item.Horse != null ? item.Horse.HorseName : null,
                IsMandatory = item.IsMandatory, Status = item.Status.ToString(), DueDate = item.DueDate,
                LeadTimeDays = item.DueDate.HasValue ? booking.RequestedDepartureDate.DayNumber - item.DueDate.Value.DayNumber : null,
                Instruction = item.Instruction
            }).ToListAsync(cancellationToken);
        var openRequests = await _db.DocumentRequests.AsNoTracking().Where(item =>
                item.DossierRequirement.ComplianceDossierId == dossier.ComplianceDossierId && !item.DossierRequirement.IsDeleted &&
                item.Status == DocumentRequestStatus.OPEN)
            .OrderByDescending(item => item.RequestedAt).ThenByDescending(item => item.DocumentRequestId).ToListAsync(cancellationToken);
        var latestRequests = openRequests.GroupBy(item => item.DossierRequirementId)
            .ToDictionary(group => group.Key, group => MapDocumentRequest(group.First()));
        foreach (var requirement in requirements)
            requirement.OpenDocumentRequest = latestRequests.GetValueOrDefault(requirement.DossierRequirementId);
        var fulfilled = requirements.Count(item => item.Status == nameof(DossierRequirementStatus.SATISFIED) ||
            item.Status == nameof(DossierRequirementStatus.WAIVED));
        var filtered = requirements.Where(item => (!status.HasValue || item.Status == status.Value.ToString()) &&
            (!request.HorseId.HasValue || item.HorseId == request.HorseId)).ToList();
        return new Response.OverviewResponse
        {
            IsInitialized = true, BookingId = booking.BookingId, BookingCode = booking.BookingCode,
            BookingSummary = await BuildBookingSummaryAsync(booking.BookingId, cancellationToken),
            ComplianceDossierId = dossier.ComplianceDossierId, DossierCode = dossier.DossierCode, Status = dossier.Status.ToString(),
            FulfilledCount = fulfilled, RequirementCount = requirements.Count,
            CompletionPercent = requirements.Count == 0 ? 0 : 100m * fulfilled / requirements.Count,
            TripRequirements = filtered.Where(item => !item.HorseId.HasValue).ToList(),
            HorseRequirements = filtered.Where(item => item.HorseId.HasValue).GroupBy(item => item.HorseId!.Value)
                .Select(group => new Response.HorseRequirementsResponse
                {
                    HorseId = group.Key, HorseName = group.First().HorseName ?? string.Empty, Requirements = group.ToList()
                }).OrderBy(item => item.HorseName).ThenBy(item => item.HorseId).ToList(),
            Actions = ViewActions(role)
        };
    }

    private async Task<Response.BookingSummaryResponse> BuildBookingSummaryAsync(Guid bookingId, CancellationToken cancellationToken) =>
        await _db.Bookings.AsNoTracking().Where(item => item.BookingId == bookingId)
            .Select(item => new Response.BookingSummaryResponse
            {
                BookingStatus = item.Status.ToString(), OriginCountry = item.OriginCountry, DestinationCountry = item.DestinationCountry,
                RequestedDepartureDate = item.RequestedDepartureDate, CustomerId = item.CustomerId, CustomerName = item.Customer.FullName,
                Horses = item.BookingHorses.OrderBy(horse => horse.Horse.HorseName).ThenBy(horse => horse.HorseId)
                    .Select(horse => new Response.HorseSummaryResponse { HorseId = horse.HorseId, HorseName = horse.Horse.HorseName }).ToList()
            }).SingleAsync(cancellationToken);

    private static DossierRequirementStatus? ValidateViewFilter(Request.ViewRequest request)
    {
        if (request.HorseId == Guid.Empty)
            throw new BadRequestException("HorseId must not be empty.");
        if (string.IsNullOrWhiteSpace(request.Status))
            return null;
        if (!Enum.TryParse<DossierRequirementStatus>(request.Status.Trim(), true, out var status) || !Enum.IsDefined(status))
            throw new BadRequestException("Status is invalid.", "INVALID_STATUS_PARAMETER");
        return status;
    }

    private static Response.ActionsResponse ViewActions(EmployeeRole? role) => new()
    {
        CanUpload = role is null, CanReview = role == EmployeeRole.TRANSPORT_SPECIALIST
    };
}
