using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using Outsourcing = Pegaxus.Service.RouteOutsourcingService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/route-plans/{routePlanId:guid}/segments/{routePlanRouteId:guid}/outsourcing")]
public sealed class RouteOutsourcingController(Outsourcing.IService service) : ControllerBase
{
    /// <summary>
    /// Assign or remove outsourced transport for a segment.
    /// </summary>
    /// <remarks>Logistics Managers only. Supply the route plan's latest lastUpdatedAt. Outsourcing clears internal personnel and vehicles, validates the ACTIVE provider's mode and maintains plan provider links atomically. Only DRAFT routes can change; route status remains DRAFT.</remarks>
    /// <param name="routePlanId">The route plan identifier (UUID).</param>
    /// <param name="routePlanRouteId">The segment identifier belonging to this route plan (UUID).</param>
    /// <param name="request">The outsourcing selection, provider, note, and latest route plan timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated segment and cleared-resource flags; 409 for locked routes, inactive providers or stale changes; 422 for a provider mode mismatch.</returns>
    [HttpPut]
    public async Task<IActionResult> Set(Guid routePlanId, Guid routePlanRouteId, [FromBody] Outsourcing.Request request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        return Ok(ResponseBuilder.SuccessResponse(await service.SetAsync(accountId, routePlanId, routePlanRouteId, request, cancellationToken),
            request.Outsourced == true ? "Outsourcing assigned successfully" : "Outsourcing removed successfully", traceId: HttpContext.TraceIdentifier));
    }
}
