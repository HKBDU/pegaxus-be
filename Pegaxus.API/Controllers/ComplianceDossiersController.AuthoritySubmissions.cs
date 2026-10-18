using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
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
            AccountId, dossierId, cancellationToken);

        return Success(response, "Authority submissions retrieved successfully");
    }

    /// <summary>
    /// Record an authority submission.
    /// </summary>
    /// <remarks>Transport Specialists record a dossier submission using JSON. Includes the authority, submission reference, submission time, and the publicId of the proof file (PDF/JPG/PNG up to 2 MB) uploaded through /api/v1/uploads/signature.</remarks>
    /// <param name="dossierId">The compliance dossier identifier (UUID).</param>
    /// <param name="request">The authority submission details and proof file publicId.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the authority submission record.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("{dossierId:guid}/authority-submissions")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("application/json")]
    public async Task<IActionResult> RecordAuthoritySubmission(
        Guid dossierId,
        [FromBody] ComplianceDossierService.Request.RecordAuthoritySubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthoritySubmissionAsync(
            AccountId, dossierId, request, cancellationToken);

        return SuccessCreated(response, "Authority submission recorded successfully");
    }

    /// <summary>
    /// Record an authority decision.
    /// </summary>
    /// <remarks>Transport Specialists submit APPROVED, REJECTED, or RETURNED decisions using JSON and a required decision file publicId (PDF/JPG/PNG up to 2 MB) uploaded through /api/v1/uploads/signature. Updates dossier status according to this decision and other mandatory submissions.</remarks>
    /// <param name="submissionId">The authority submission identifier (UUID).</param>
    /// <param name="request">The decision, decision file publicId, and decision-specific permit number, rejection reason, or revision deadline.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the authority decision and updated dossier status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPatch("/api/v1/authority-submissions/{submissionId:guid}/decision")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    [Consumes("application/json")]
    public async Task<IActionResult> RecordAuthorityDecision(
        Guid submissionId,
        [FromBody] ComplianceDossierService.Request.RecordAuthorityDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecordAuthorityDecisionAsync(
            AccountId, submissionId, request, cancellationToken);

        return Success(response, "Authority decision recorded successfully for ComplianceDossier");
    }
}
