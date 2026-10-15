using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
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
        SuccessCreated(await _service.UploadDocumentAsync(AccountId, dossierId, requirementId, request, cancellationToken), "ComplianceDocument uploaded successfully (Version v1). Pending DocumentReview by Transport Specialist.");

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
        var response = await _service.SubmitDocumentVersionAsync(AccountId, dossierId, requirementId, request, cancellationToken);
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
        Success(await _service.UpdateDocumentMetadataAsync(AccountId, dossierId, requirementId, request, cancellationToken), "ComplianceDocument metadata updated successfully.");

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
        Success(await _service.GetDocumentVersionsAsync(AccountId, dossierId, requirementId, cancellationToken), "DocumentVersion history retrieved successfully.");

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
        SuccessCreated(await _service.AddDocumentRequestCommentAsync(AccountId, dossierId, requirementId, requestId, request, cancellationToken), "DocumentRequest comment added successfully.");

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
        Success(await _service.GetDocumentRequestCommentsAsync(AccountId, dossierId, requirementId, requestId, cancellationToken), "DocumentRequest comments retrieved successfully.");

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
        Success(await _service.GetMissingRequirementsAsync(AccountId, dossierId, cancellationToken), "Danh sách giấy tờ còn thiếu.");

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
        var result = await _service.RequestMissingDocumentsAsync(AccountId, dossierId, request, cancellationToken);
        return SuccessCreated(result, result.EmailSent ? "Đã gửi thông báo nhắc nộp bổ sung giấy tờ thành công cho khách hàng." :
                "Đã lưu thông báo Web; email nhắc nộp giấy tờ đang chờ gửi lại.");
    }

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
        var file = await _service.GetDocumentPreviewAsync(AccountId, dossierId, requirementId, versionId, attachmentId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        Response.Headers["Content-Disposition"] = "inline";
        return File(file.Content, file.ContentType);
    }
}
