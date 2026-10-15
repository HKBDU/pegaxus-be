using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using Catalog = Pegaxus.Service.ComplianceRequirementService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compliance-requirements")]
public sealed class ComplianceRequirementsController(Catalog.IComplianceRequirementService service) : ApiControllerBase
{
    /// <summary>
    /// List compliance requirements.
    /// </summary>
    /// <remarks>Transport Specialists and Logistics Managers may filter the catalog by status, category, country, BCP code, search, and sorting.</remarks>
    /// <param name="request">The catalog filters, sort order, and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated compliance requirement catalog.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetList([FromQuery] Catalog.Request.ListRequest request, CancellationToken cancellationToken)
    {
        var result = await service.GetListAsync(request, cancellationToken);
        return Success(result, "ComplianceRequirements retrieved successfully.");
    }

    /// <summary>
    /// Get a compliance requirement.
    /// </summary>
    /// <remarks>Includes reference counts from dossier templates and dossier requirements.</remarks>
    /// <param name="id">The compliance requirement identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with requirement details and reference counts.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrLogisticsManagerPolicy)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return Success(result, "ComplianceRequirement retrieved successfully.");
    }

    /// <summary>
    /// Create a compliance requirement.
    /// </summary>
    /// <remarks>Transport Specialists create entries with a unique requirementCode. Duplicate codes return 409 DUPLICATE_REQUIREMENT_CODE.</remarks>
    /// <param name="request">The requirement code, name, category, status, validity rules, and optional legal details.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new compliance requirement.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Create([FromBody] Catalog.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, AccountId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.ComplianceRequirementId }, ResponseBuilder.SuccessResponse(
            result, "ComplianceRequirement entry saved successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a compliance requirement.
    /// </summary>
    /// <remarks>Referenced entries require acknowledgeReferenceImpact = true; otherwise, the response is 409 with reference counts.</remarks>
    /// <param name="id">The compliance requirement identifier (UUID).</param>
    /// <param name="request">The replacement requirement details and reference-impact acknowledgment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated requirement, or 409 when reference impact is not acknowledged.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Update(Guid id, [FromBody] Catalog.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.UpdateAsync(id, request, AccountId, cancellationToken);
            return Success(result, "ComplianceRequirement updated successfully.");
        }
        catch (Catalog.Service.ReferenceImpactException exception)
        {
            var response = ResponseBuilder.ErrorResponse(null, exception.Message, exception.ErrorCode,
                StatusCodes.Status409Conflict, HttpContext.TraceIdentifier);
            response.Value = new { exception.ReferenceCount };
            return Conflict(response);
        }
    }

    /// <summary>
    /// Change a compliance requirement status.
    /// </summary>
    /// <remarks>Supports ACTIVE and INACTIVE. Inactive requirements cannot be used for new dossier checklist entries.</remarks>
    /// <param name="id">The compliance requirement identifier (UUID).</param>
    /// <param name="request">The target ACTIVE or INACTIVE status.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the status is changed.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] Catalog.Request.StatusRequest request, CancellationToken cancellationToken)
    {
        await service.UpdateStatusAsync(id, request, AccountId, cancellationToken);
        var message = request.Status.Trim().Equals("INACTIVE", StringComparison.OrdinalIgnoreCase)
            ? "ComplianceRequirement deactivated successfully." : "ComplianceRequirement activated successfully.";
        return Success(null, message);
    }

    /// <summary>
    /// Delete a compliance requirement.
    /// </summary>
    /// <remarks>Soft-deletes an unreferenced entry. Returns 409 REQUIREMENT_IN_USE when references exist; deactivate the requirement instead.</remarks>
    /// <param name="id">The compliance requirement identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the requirement is soft-deleted.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, AccountId, cancellationToken);
        return Success(null, "ComplianceRequirement deleted successfully.");
    }

}
