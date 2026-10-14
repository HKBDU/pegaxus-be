using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
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
        SuccessCreated(await _service.ApproveDocumentAsync(AccountId, dossierId, requirementId, versionId, request, cancellationToken), "DocumentReview recorded: APPROVED. DossierRequirement status updated to SATISFIED by TS.");

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
        SuccessCreated(await _service.RevokeDocumentReviewAsync(AccountId, dossierId, requirementId, versionId, reviewId, request, cancellationToken), "DocumentReview revoked successfully. DossierRequirement status updated by TS.");

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
        Success(await _service.MarkReadyForSubmissionAsync(AccountId, dossierId, cancellationToken), "ComplianceDossier marked READY_FOR_SUBMISSION by TS. No authority submission was created.");

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
        SuccessCreated(await _service.RejectDocumentAsync(AccountId, dossierId, requirementId, versionId, request, cancellationToken), "DocumentReview (REJECTED) recorded. Status updated by TS. DocumentRequest sent to customer.");
}
