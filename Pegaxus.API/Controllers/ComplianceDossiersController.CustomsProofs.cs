using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

public sealed partial class ComplianceDossiersController
{
    /// <summary>
    /// Submit customs clearance evidence.
    /// </summary>
    /// <remarks>The assigned Driver submits customs proof images using JSON publicId references from presigned uploads. Creates a proof awaiting Transport Specialist verification without changing dossier status.</remarks>
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
    [Consumes("application/json")]
    public async Task<IActionResult> SubmitCustomsClearanceProof(
        Guid dossierId,
        [FromBody] ComplianceDossierService.Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken)
    {
        var proofId = await _service.SubmitCustomsClearanceProofAsync(
            AccountId, dossierId, request, cancellationToken);

        return SuccessCreated(new { CustomsClearanceProofId = proofId }, "Customs proof submitted successfully. Awaiting TS Verification.");
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
            AccountId, proofId, cancellationToken);

        return Success(response, "CustomsClearanceProof retrieved successfully");
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
            AccountId, proofId, request, cancellationToken);

        return Success(response, "Customs proof finalized successfully");
    }
}
