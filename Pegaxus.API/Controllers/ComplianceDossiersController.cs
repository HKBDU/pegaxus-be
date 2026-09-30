using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;

using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
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
