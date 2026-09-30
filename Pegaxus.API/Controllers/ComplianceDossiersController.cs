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

    [HttpPost("/api/v1/compliance-dossiers/{dossierId:guid}/quarantine-proofs")]
    [Authorize(Roles = "Driver,Escort")]
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
