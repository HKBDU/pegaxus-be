using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using DossierTemplateService = Pegaxus.Service.DossierTemplateService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/dossier-templates")]
[Authorize(Roles = "TransportSpecialist,Admin")]
public sealed class DossierTemplatesController(DossierTemplateService.IService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] DossierTemplateService.Request.ListRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.ListRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.GetListAsync(request, cancellationToken),
            "DossierTemplates retrieved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{templateId:guid}")]
    public async Task<IActionResult> Get(Guid templateId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.GetAsync(templateId, cancellationToken),
            "DossierTemplate retrieved successfully.", traceId: HttpContext.TraceIdentifier));

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

    [HttpPut("{templateId:guid}")]
    public async Task<IActionResult> Update(Guid templateId, [FromBody] DossierTemplateService.Request.SaveRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.SaveRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.UpdateAsync(templateId, request, cancellationToken),
            "DossierTemplate saved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("{templateId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid templateId, [FromBody] DossierTemplateService.Request.ChangeStatusRequest request,
        [FromServices] IValidator<DossierTemplateService.Request.ChangeStatusRequest> validator, CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        return Ok(ResponseBuilder.SuccessResponse(await service.ChangeStatusAsync(templateId, request, cancellationToken),
            "DossierTemplate status updated successfully.", traceId: HttpContext.TraceIdentifier));
    }

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
