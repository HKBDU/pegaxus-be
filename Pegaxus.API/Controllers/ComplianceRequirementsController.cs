using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using Catalog = Pegaxus.Service.ComplianceRequirementService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/compliance-requirements")]
public sealed class ComplianceRequirementsController(Catalog.IService service) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var canRead = HttpMethods.IsGet(Request.Method) && User.IsInRole("LogisticsManager");
        if (!canRead && !User.IsInRole("TransportSpecialist") && !User.IsInRole("Admin"))
            throw new ForbiddenException("Access denied. Only Transport Specialists can modify ComplianceRequirements.");
        base.OnActionExecuting(context);
    }

    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] Catalog.Request.ListRequest request, CancellationToken cancellationToken)
    {
        var result = await service.GetListAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(result, "ComplianceRequirements retrieved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(result, "ComplianceRequirement retrieved successfully.", traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Catalog.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, GetAccountId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.ComplianceRequirementId }, ResponseBuilder.SuccessResponse(
            result, "ComplianceRequirement entry saved successfully.", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] Catalog.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.UpdateAsync(id, request, GetAccountId(), cancellationToken);
            return Ok(ResponseBuilder.SuccessResponse(result, "ComplianceRequirement updated successfully.", traceId: HttpContext.TraceIdentifier));
        }
        catch (Catalog.Service.ReferenceImpactException exception)
        {
            var response = ResponseBuilder.ErrorResponse(null, exception.Message, exception.ErrorCode,
                StatusCodes.Status409Conflict, HttpContext.TraceIdentifier);
            response.Value = new { exception.ReferenceCount };
            return Conflict(response);
        }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] Catalog.Request.StatusRequest request, CancellationToken cancellationToken)
    {
        await service.UpdateStatusAsync(id, request, GetAccountId(), cancellationToken);
        var message = request.Status.Trim().Equals("INACTIVE", StringComparison.OrdinalIgnoreCase)
            ? "ComplianceRequirement deactivated successfully." : "ComplianceRequirement activated successfully.";
        return Ok(ResponseBuilder.SuccessResponse(null, message, traceId: HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, GetAccountId(), cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "ComplianceRequirement deleted successfully.", traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedException("The authenticated account identifier is invalid");
    }
}
