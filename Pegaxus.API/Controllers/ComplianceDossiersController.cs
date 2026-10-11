using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compliance-dossiers")]
public sealed class ComplianceDossiersController : ControllerBase
{
    private readonly ComplianceDossierService.IService _service;

    public ComplianceDossiersController(ComplianceDossierService.IService service)
    {
        _service = service;
    }

    /// <summary>
    /// Approve a document version.
    /// </summary>
    /// <remarks>Transport Specialists confirm metadata and submit versionUploadedAt and documentUpdatedAt to detect concurrent changes. Approval marks the requirement SATISFIED; expired documents require an exception reason.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="versionId">The document version identifier (UUID).</param>
    /// <param name="request">The metadata confirmation, concurrency timestamps, optional review note, and expiry exception reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the document review and updated requirement status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/approve")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> ApproveDocument(Guid dossierId, Guid requirementId, Guid versionId,
        [FromBody] ComplianceDossierService.Request.ApproveDocumentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.ApproveDocumentAsync(GetAccountId(), dossierId, requirementId, versionId, request, cancellationToken),
            "DocumentReview recorded: APPROVED. DossierRequirement status updated to SATISFIED by TS.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Revoke a document approval.
    /// </summary>
    /// <remarks>Transport Specialists revoke the current approval while preserving review history. The target requirement status must be PROVIDED or REJECTED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="versionId">The document version identifier (UUID).</param>
    /// <param name="reviewId">The document review identifier (UUID).</param>
    /// <param name="request">The target requirement status and required revocation reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the revocation review and updated requirement status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/reviews/{reviewId:guid}/revoke")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RevokeDocumentReview(Guid dossierId, Guid requirementId, Guid versionId, Guid reviewId,
        [FromBody] ComplianceDossierService.Request.RevokeDocumentReviewRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.RevokeDocumentReviewAsync(GetAccountId(), dossierId, requirementId, versionId, reviewId, request, cancellationToken),
            "DocumentReview revoked successfully. DossierRequirement status updated by TS.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Mark a dossier as ready for submission.
    /// </summary>
    /// <remarks>Transport Specialists may mark the dossier READY_FOR_SUBMISSION only when all mandatory requirements are SATISFIED or WAIVED. Does not create an authority submission.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier readiness result.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/ready-for-submission")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> MarkReadyForSubmission(Guid dossierId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.MarkReadyForSubmissionAsync(GetAccountId(), dossierId, cancellationToken),
            "ComplianceDossier marked READY_FOR_SUBMISSION by TS. No authority submission was created.", traceId: HttpContext.TraceIdentifier));
    /// <summary>
    /// Upload documents for a dossier requirement.
    /// </summary>
    /// <remarks>Customers upload each file through /uploads/signature and /uploads/confirm, then submit JSON with 1-10 ordered files containing publicId only. The backend verifies ownership and Cloudinary metadata, scans file content, and returns signed read URLs expiring after five minutes.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The files, document metadata, and optional upload note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the document version, file details, and presigned read URLs.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/document")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    [Consumes("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UploadDocument(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.DocumentUploadRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.UploadDocumentAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "ComplianceDocument uploaded successfully (Version v1). Pending DocumentReview by Transport Specialist.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));


    /// <summary>
    /// Update or replace uploaded documents.
    /// </summary>
    /// <remarks>Customers submit JSON with 1-10 ordered Cloudinary publicId references, never file binaries or client-provided URLs. An unreviewed version is replaced; reviewed versions are preserved and a new version is created. Signed read URLs expire after five minutes.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The replacement files, document metadata, and optional upload note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created for a new version, or 200 OK when replacing an unreviewed version, with presigned read URLs.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/document/versions")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    [Consumes("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SubmitDocumentVersion(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.DocumentUploadRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.SubmitDocumentVersionAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken);
        var status = response.IsNewVersion ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        var message = response.IsNewVersion
            ? $"New DocumentVersion (v{response.VersionNo}) submitted successfully. Pending DocumentReview."
            : $"DocumentVersion (v{response.VersionNo}) updated successfully. Pending DocumentReview.";
        return StatusCode(status, ResponseBuilder.SuccessResponse(response, message, status, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update document metadata.
    /// </summary>
    /// <remarks>Customers may change documentNumber, issuingAuthority, issuedDate, and expiryDate without uploading files. Dossier and requirement state restrictions still apply.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The document metadata fields to update.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated document metadata.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}/document/metadata")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> UpdateDocumentMetadata(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.DocumentMetadataRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.UpdateDocumentMetadataAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "ComplianceDocument metadata updated successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Get document version history.
    /// </summary>
    /// <remarks>Customers may view owned dossiers; Transport Specialists and Logistics Managers may view accessible dossiers. Returns versions, reviews, and file readUrl/readUrlExpiresAt values with five-minute presigned GET URLs.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with document versions, reviews, and presigned file URLs.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">File storage is unavailable or a presigned URL cannot be generated.</exception>
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/document/versions")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetDocumentVersions(Guid dossierId, Guid requirementId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.GetDocumentVersionsAsync(GetAccountId(), dossierId, requirementId, cancellationToken),
            "DocumentVersion history retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Reject a document version.
    /// </summary>
    /// <remarks>Transport Specialists record a rejection review, update requirement and dossier statuses, and create a DocumentRequest for customer corrections. Concurrency timestamps must match the current file and metadata.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="versionId">The document version identifier (UUID).</param>
    /// <param name="request">The error category, detailed rejection note, revision deadline, target statuses, and concurrency timestamps.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the rejection review and document correction request.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/reject")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RejectDocument(Guid dossierId, Guid requirementId, Guid versionId,
        [FromBody] ComplianceDossierService.Request.RejectDocumentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.RejectDocumentAsync(GetAccountId(), dossierId, requirementId, versionId, request, cancellationToken),
            "DocumentReview (REJECTED) recorded. Status updated by TS. DocumentRequest sent to customer.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Add a document request comment.
    /// </summary>
    /// <remarks>The owning customer and Transport Specialists may comment on an OPEN document request.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="requestId">The document correction request identifier (UUID).</param>
    /// <param name="request">The comment message, between 1 and 2000 characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new comment.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements/{requirementId:guid}/document-requests/{requestId:guid}/comments")]
    [Authorize(Policy = JwtExtensions.CustomerOrTransportSpecialistPolicy)]
    public async Task<IActionResult> AddDocumentRequestComment(Guid dossierId, Guid requirementId, Guid requestId,
        [FromBody] ComplianceDossierService.Request.DocumentRequestCommentRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.AddDocumentRequestCommentAsync(GetAccountId(), dossierId, requirementId, requestId, request, cancellationToken),
            "DocumentRequest comment added successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Get document request comments.
    /// </summary>
    /// <remarks>Only the owning customer and Transport Specialists may read the discussion.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="requestId">The document correction request identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with document request comments.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/document-requests/{requestId:guid}/comments")]
    [Authorize(Policy = JwtExtensions.CustomerOrTransportSpecialistPolicy)]
    public async Task<IActionResult> GetDocumentRequestComments(Guid dossierId, Guid requirementId, Guid requestId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetDocumentRequestCommentsAsync(GetAccountId(), dossierId, requirementId, requestId, cancellationToken),
            "DocumentRequest comments retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// List missing dossier requirements.
    /// </summary>
    /// <remarks>Transport Specialists retrieve the requirements that still need customer documents.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the missing requirement list.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The dossier is not in a state that permits missing-document reminders.</exception>
    [HttpGet("{dossierId:guid}/missing-requirements")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> GetMissingRequirements(Guid dossierId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetMissingRequirementsAsync(GetAccountId(), dossierId, cancellationToken),
            "Danh sách giấy tờ còn thiếu.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Send a missing-document reminder.
    /// </summary>
    /// <remarks>Transport Specialists select requirementIds and a dueDate to notify the customer. Check emailSent: the web notification can be saved while email delivery awaits a retry.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The missing requirement identifiers, due date, and optional note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the reminder and email delivery result.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/missing-document-reminders")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RequestMissingDocuments(Guid dossierId,
        [FromBody] ComplianceDossierService.Request.MissingDocumentsRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RequestMissingDocumentsAsync(GetAccountId(), dossierId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(result,
            result.EmailSent ? "Đã gửi thông báo nhắc nộp bổ sung giấy tờ thành công cho khách hàng." :
                "Đã lưu thông báo Web; email nhắc nộp giấy tờ đang chờ gửi lại.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Get a compliance dossier by booking.
    /// </summary>
    /// <remarks>Customers may view only their own bookings. If the dossier has not been initialized, returns an overview explaining that initialization is pending.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The optional requirement status and horse filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier overview or initialization-pending message.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetByBooking(Guid bookingId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetByBookingAsync(GetAccountId(), bookingId, request, cancellationToken),
            "ComplianceDossier retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Get compliance dossier details.
    /// </summary>
    /// <remarks>Customers may view only their own dossiers; Transport Specialists and Logistics Managers may view accessible dossiers. SATISFIED and WAIVED requirements count toward completion.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The optional requirement status and horse filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier overview and filtered checklist.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetDossier(Guid dossierId, [FromQuery] ComplianceDossierService.Request.ViewRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetDossierAsync(GetAccountId(), dossierId, request, cancellationToken),
            "ComplianceDossier retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Get dossier requirement details.
    /// </summary>
    /// <remarks>Includes the requirement, document, versions, reviews, and correction requests. Customers may only view requirements in their own dossiers.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the requirement and associated document details.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    public async Task<IActionResult> GetRequirement(Guid dossierId, Guid requirementId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetRequirementAsync(GetAccountId(), dossierId, requirementId, cancellationToken),
            "DossierRequirement retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Preview a document file.
    /// </summary>
    /// <remarks>Returns the main version file, or an attachment when attachmentId is supplied. Access is scoped to the dossier. Responds inline with the file content type and disables caching; does not return a JSON envelope.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="versionId">The document version identifier (UUID).</param>
    /// <param name="attachmentId">The attachment identifier (UUID), or null to preview the main version file.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the requested file content.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/preview")]
    [HttpGet("{dossierId:guid}/requirements/{requirementId:guid}/versions/{versionId:guid}/attachments/{attachmentId:guid}/preview")]
    [Authorize(Policy = JwtExtensions.ComplianceDossierViewerPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetDocumentPreview(Guid dossierId, Guid requirementId, Guid versionId,
        Guid? attachmentId, CancellationToken cancellationToken)
    {
        var file = await _service.GetDocumentPreviewAsync(GetAccountId(), dossierId, requirementId, versionId, attachmentId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        Response.Headers["Content-Disposition"] = "inline";
        return File(file.Content, file.ContentType);
    }
    /// <summary>
    /// Suggest dossier templates for a booking.
    /// </summary>
    /// <remarks>Transport Specialists retrieve ACTIVE, currently effective templates whose requirements are ACTIVE, ranked by country relevance to the booking.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with available templates and suggestions.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpGet("/api/v1/bookings/{bookingId:guid}/dossier-templates")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> GetTemplateOptions(Guid bookingId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.GetTemplateOptionsAsync(GetAccountId(), bookingId, cancellationToken),
            "Available DossierTemplates retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Preview a compliance dossier.
    /// </summary>
    /// <remarks>Transport Specialists submit a template or custom items for an approved transport. Generates a prospective checklist without saving a dossier.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The selected template or custom requirement items and optional booking concurrency timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the generated dossier preview.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("/api/v1/transports/{transportId:guid}/compliance-dossier/preview")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Preview(Guid transportId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await _service.PreviewAsync(GetAccountId(), transportId, request, cancellationToken),
            "ComplianceDossier preview generated successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Initialize a compliance dossier.
    /// </summary>
    /// <remarks>Transport Specialists initialize a dossier from a template or custom items for an approved booking. Creates the checklist and notifies the customer.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The selected template or custom requirement items, optional note, and booking concurrency timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the dossier and generated requirements.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("/api/v1/bookings/{bookingId:guid}/compliance-dossier")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Initialize(Guid bookingId, [FromBody] ComplianceDossierService.Request.InitializeRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.InitializeAsync(GetAccountId(), bookingId, request, cancellationToken),
            "ComplianceDossier initialized successfully. DossierRequirements generated for customer.",
            StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Add a dossier requirement manually.
    /// </summary>
    /// <remarks>Transport Specialists add an ACTIVE compliance requirement. horseId = null applies to the whole trip; otherwise the horse must belong to the booking. Creates a PENDING requirement and notifies the customer. Errors include REQUIREMENT_INACTIVE, HORSE_NOT_IN_BOOKING, DR_DUPLICATE (including a null horse), and DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The required complianceRequirementId (UUID) and isMandatory; optional horseId, dueDate (yyyy-MM-dd), and instruction. Legacy leadTimeDays and reason remain supported; do not send both dueDate and leadTimeDays.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new dossier requirement and PENDING status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{dossierId:guid}/requirements")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> AddRequirement(Guid dossierId, [FromBody] ComplianceDossierService.Request.RequirementRequest request,
        CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            await _service.AddRequirementAsync(GetAccountId(), dossierId, request, cancellationToken),
            "DossierRequirement added successfully", StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>
    /// Waive a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists mark the requirement WAIVED without removing it from the checklist. It counts toward completion; the operation records an audit event and notifies the customer. Errors include WAIVE_REASON_REQUIRED and DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The required waivedReason, containing at most 2000 characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the dossier requirement in WAIVED status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}/waive")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> WaiveRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.WaiveRequirementRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.WaiveRequirementAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "DossierRequirement waived successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Update a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists may change supplied fields while the dossier is editable. A mandatory requirement cannot be downgraded to optional. Locked dossiers return 409 DOSSIER_LOCKED.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">At least one of isMandatory, leadTimeDays, or instruction, plus a required reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated dossier requirement.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> UpdateRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.UpdateRequirementRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.UpdateRequirementAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "DossierRequirement updated successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Remove a dossier requirement.
    /// </summary>
    /// <remarks>Transport Specialists soft-delete optional requirements only. Mandatory requirements cannot be removed. Locked dossiers return 409 DOSSIER_LOCKED; successful changes are audited and the customer is notified.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="requirementId">The dossier requirement identifier (UUID).</param>
    /// <param name="request">The JSON body containing the required removal reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the removed requirement identifier and updated completion counts.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpDelete("{dossierId:guid}/requirements/{requirementId:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RemoveRequirement(Guid dossierId, Guid requirementId,
        [FromBody] ComplianceDossierService.Request.RemoveRequirementRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(
            await _service.RemoveRequirementAsync(GetAccountId(), dossierId, requirementId, request, cancellationToken),
            "DossierRequirement removed successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Get the compliance dossier dashboard.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers view dossier KPIs, checklist progress, and risk filters across the system.</remarks>
    /// <param name="request">The dashboard filters, sorting, and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with dashboard metrics and a paginated dossier list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet("dashboard")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetDashboardAsync(
            GetAccountId(), request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Dashboard retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Export the compliance dossier dashboard.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers export all matching results as XLSX or PDF. Returns a binary file without a JSON response envelope.</remarks>
    /// <param name="request">The dashboard filters, sorting, and XLSX or PDF export format.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the generated report file.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet("dashboard/export")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> ExportDashboard(
        [FromQuery] ComplianceDossierService.Request.DashboardExportRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ExportDashboardAsync(
            GetAccountId(), request, cancellationToken);

        return File(response.Content, response.ContentType, response.FileName);
    }

    /// <summary>
    /// Get compliance dossier audit history.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers view append-only audit events with action, actor, time, and pagination filters.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The audit action, actor, date range, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the filtered audit history.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/audit-history")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetAuditHistory(
        Guid dossierId,
        [FromQuery] ComplianceDossierService.Request.AuditHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAuditHistoryAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Audit history retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// List authority submissions.
    /// </summary>
    /// <remarks>Returns dossier submission records newest first, including the submitting account and evidence file details. Access is checked against the dossier.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with authority submission records.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{dossierId:guid}/authority-submissions")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetAuthoritySubmissions(
        Guid dossierId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAuthoritySubmissionsAsync(
            GetAccountId(), dossierId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Authority submissions retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Record an authority submission.
    /// </summary>
    /// <remarks>Transport Specialists record a dossier submission using multipart/form-data. Includes the authority, submission reference, submission time, and required proof file.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The authority submission details and proof file.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the authority submission record.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("{dossierId:guid}/authority-submissions")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RecordAuthoritySubmission(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.RecordAuthoritySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthoritySubmissionAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Authority submission recorded successfully",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Record an authority decision.
    /// </summary>
    /// <remarks>Transport Specialists submit APPROVED, REJECTED, or RETURNED decisions using multipart/form-data and a required decision file. Updates dossier status according to this decision and other mandatory submissions.</remarks>
    /// <param name="submissionId">The authority submission identifier (UUID).</param>
    /// <param name="request">The decision, decision file, and decision-specific permit number, rejection reason, or revision deadline.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the authority decision and updated dossier status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPatch("/api/v1/authority-submissions/{submissionId:guid}/decision")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> RecordAuthorityDecision(
        Guid submissionId,
        [FromForm] ComplianceDossierService.Request.RecordAuthorityDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthorityDecisionAsync(
            GetAccountId(), submissionId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Authority decision recorded successfully for ComplianceDossier",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Submit quarantine evidence.
    /// </summary>
    /// <remarks>The assigned Driver or Escort submits BCP proof images using multipart/form-data. Creates a QuarantineProof awaiting Transport Specialist verification without changing dossier status.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The BCP information, evidence files, and optional notes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the quarantine proof identifier.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("/api/v1/compliance-dossiers/{dossierId:guid}/quarantine-proofs")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitQuarantineProof(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.SubmitQuarantineProofRequest request,
        CancellationToken cancellationToken)
    {
        var proofId = await _service.SubmitQuarantineProofAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            new { QuarantineProofId = proofId },
            "Quarantine proof submitted successfully. Awaiting TS Verification.",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Get quarantine proof details.
    /// </summary>
    /// <remarks>Returns proof status, evidence files, and horse microchip information to an authorized dossier participant.</remarks>
    /// <param name="proofId">The proof identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with quarantine proof details.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("/api/v1/quarantine-proofs/{proofId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetQuarantineProof(
        Guid proofId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetQuarantineProofAsync(
            GetAccountId(), proofId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "QuarantineProof retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Finalize quarantine verification.
    /// </summary>
    /// <remarks>Transport Specialists verify horse microchips and record PASSED, PASSED_WITH_CONDITIONS, or FAILED using multipart/form-data. REJECT_PROOF returns the evidence for resubmission.</remarks>
    /// <param name="proofId">The proof identifier (UUID).</param>
    /// <param name="request">The verification action, horse checks, result, and supporting files or rejection reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the quarantine verification result.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPatch("/api/v1/quarantine-proofs/{proofId:guid}/finalize")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> FinalizeQuarantineProof(
        Guid proofId,
        [FromForm] ComplianceDossierService.Request.FinalizeQuarantineProofRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.FinalizeQuarantineProofAsync(
            GetAccountId(), proofId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Quarantine proof finalized successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Submit customs clearance evidence.
    /// </summary>
    /// <remarks>The assigned Driver submits customs proof images using multipart/form-data. Creates a proof awaiting Transport Specialist verification without changing dossier status.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The customs evidence files, ATA Carnet information, and optional notes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the customs clearance proof identifier.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("/api/v1/compliance-dossiers/{dossierId:guid}/customs-clearance-proofs")]
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitCustomsClearanceProof(
        Guid dossierId,
        [FromForm] ComplianceDossierService.Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken)
    {
        var proofId = await _service.SubmitCustomsClearanceProofAsync(
            GetAccountId(), dossierId, request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            new { CustomsClearanceProofId = proofId },
            "Customs proof submitted successfully. Awaiting TS Verification.",
            StatusCodes.Status201Created,
            HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Get customs clearance proof details.
    /// </summary>
    /// <remarks>Returns proof status, evidence files, and ATA Carnet information to an authorized dossier participant.</remarks>
    /// <param name="proofId">The proof identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with customs clearance proof details.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("/api/v1/customs-clearance-proofs/{proofId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetCustomsClearanceProof(
        Guid proofId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetCustomsClearanceProofAsync(
            GetAccountId(), proofId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "CustomsClearanceProof retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Finalize customs clearance.
    /// </summary>
    /// <remarks>Transport Specialists verify the MRN and customs channel, then record CLEARED, HELD, or REJECTED using multipart/form-data. REJECT_PROOF returns the evidence to the Driver for resubmission.</remarks>
    /// <param name="proofId">The proof identifier (UUID).</param>
    /// <param name="request">The verification action, MRN, customs channel, result, and supporting files or rejection reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the customs clearance result.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPatch("/api/v1/customs-clearance-proofs/{proofId:guid}/finalize")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> FinalizeCustomsClearanceProof(
        Guid proofId,
        [FromForm] ComplianceDossierService.Request.FinalizeCustomsClearanceProofRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.FinalizeCustomsClearanceProofAsync(
            GetAccountId(), proofId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Customs proof finalized successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (Guid.TryParse(value, out var accountId))
            return accountId;

        throw new UnauthorizedException("The authentication session is invalid or has expired");
    }
}
