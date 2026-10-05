using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using DossierTemplateService = Pegaxus.Service.DossierTemplateService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/dossier-templates")]
[Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
public sealed class DossierTemplatesController(DossierTemplateService.IService service) : ControllerBase
{
    /// <summary>
    /// List dossier templates.
    /// </summary>
    /// <remarks>Transport Specialists may search and filter templates. An ACTIVE filter only returns templates effective today.</remarks>
    /// <param name="request">The search, status, and pagination filters.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated template list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] DossierTemplateService.Request.ListRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.ListRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.GetListAsync(request, cancellationToken),
            "DossierTemplates retrieved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Get a dossier template.
    /// </summary>
    /// <remarks>Includes the template version and its ordered requirement items.</remarks>
    /// <param name="templateId">The dossier template identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with template details.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{templateId:guid}")]
    public async Task<IActionResult> Get(Guid templateId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.GetAsync(templateId, cancellationToken),
            "DossierTemplate retrieved successfully.", traceId: HttpContext.TraceIdentifier));

    /// <summary>
    /// Create a dossier template.
    /// </summary>
    /// <remarks>The initial status must be DRAFT or ACTIVE. At least one ACTIVE compliance requirement is required; requirement and sequence numbers must be unique within the template.</remarks>
    /// <param name="request">The template code, name, status, effective dates, and requirement items.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new template.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DossierTemplateService.Request.SaveRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.SaveRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { templateId = response.DossierTemplateId },
            ResponseBuilder.SuccessResponse(response, "DossierTemplate saved successfully.",
                StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a dossier template.
    /// </summary>
    /// <remarks>Replaces template details and items and increments versionNo. Existing dossier checklists are not automatically changed.</remarks>
    /// <param name="templateId">The dossier template identifier (UUID).</param>
    /// <param name="request">The replacement template details and complete item list.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated template.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPut("{templateId:guid}")]
    public async Task<IActionResult> Update(Guid templateId, [FromBody] DossierTemplateService.Request.SaveRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.SaveRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.UpdateAsync(templateId, request, cancellationToken),
            "DossierTemplate saved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Change a dossier template status.
    /// </summary>
    /// <remarks>Supports DRAFT to ACTIVE and ACTIVE to INACTIVE. Activation requires current effective dates and ACTIVE requirement references.</remarks>
    /// <param name="templateId">The dossier template identifier (UUID).</param>
    /// <param name="request">The target template status.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated template.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpPatch("{templateId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid templateId, [FromBody] DossierTemplateService.Request.ChangeStatusRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.ChangeStatusRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.ChangeStatusAsync(templateId, request, cancellationToken),
            "DossierTemplate status updated successfully.", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Delete a dossier template.
    /// </summary>
    /// <remarks>Soft-deletes an unused template. Returns 409 DOSSIER_TEMPLATE_IN_USE if a dossier references it; deactivate the template instead.</remarks>
    /// <param name="templateId">The dossier template identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the template is soft-deleted.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpDelete("{templateId:guid}")]
    public async Task<IActionResult> Delete(Guid templateId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId))
            throw new UnauthorizedException("The authentication session is invalid or has expired");
        await service.DeleteAsync(templateId, accountId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "DossierTemplate deleted successfully.", traceId: HttpContext.TraceIdentifier));
    }

    private async Task<IActionResult?> ValidateRequestAsync<T>(T request, IValidator<T> validator, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid) return null;
        var errors = result.Errors.Select(error => new
        {
            field = string.IsNullOrEmpty(error.PropertyName) ? "$" : char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..],
            message = error.ErrorMessage
        });
        return BadRequest(ResponseBuilder.ErrorResponse(errors, "The request data is invalid", traceId: HttpContext.TraceIdentifier));
    }
}
