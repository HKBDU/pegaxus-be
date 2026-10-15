using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
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
            AccountId, dossierId, request, cancellationToken);

        return SuccessCreated(new { QuarantineProofId = proofId }, "Quarantine proof submitted successfully. Awaiting TS Verification.");
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
            AccountId, proofId, cancellationToken);

        return Success(response, "QuarantineProof retrieved successfully");
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
            AccountId, proofId, request, cancellationToken);

        return Success(response, "Quarantine proof finalized successfully");
    }
}
