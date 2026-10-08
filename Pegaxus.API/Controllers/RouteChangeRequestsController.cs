using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using Pegaxus.Service.RouteChangeService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/route-change-requests")]
[Authorize(Policy = JwtExtensions.LogisticsManagerOrFleetRouteCoordinatorPolicy)]
public sealed class RouteChangeRequestsController(IService service) : ControllerBase
{
    /// <summary>Submit an emergency route change request.</summary>
    /// <remarks>Coordinator only. Requires IN_TRANSIT transport and an ACTIVE primary route. BR-036 approval thresholds are derived by the server.</remarks>
    /// <param name="routePlanId">The active primary route plan UUID.</param>
    /// <param name="request">One confirmed backup or an untravelled replacement itinerary.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the pending request and approval reasons.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/change-requests")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid routePlanId, [FromBody] Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(routePlanId, AccountId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { changeRequestId = result.ChangeRequestId },
            ResponseBuilder.SuccessResponse(result, "Route change request submitted successfully", 201, HttpContext.TraceIdentifier));
    }

    /// <summary>List emergency route change requests.</summary>
    /// <remarks>Manager or Coordinator only. Supports status, transport plan and creation time filters with bounded pagination.</remarks>
    /// <param name="request">Pagination, filters and sort order.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with paginated requests.</returns>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Request.ListRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.ListAsync(AccountId(), request, cancellationToken),
            "Route change requests retrieved successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>Get an emergency route change request.</summary>
    /// <remarks>Manager or Coordinator only. Returns immutable old and proposed itineraries, server comparison, costs and decision.</remarks>
    /// <param name="changeRequestId">The change request UUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the request detail.</returns>
    [HttpGet("{changeRequestId:guid}")]
    public async Task<IActionResult> Get(Guid changeRequestId, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.GetAsync(changeRequestId, AccountId(), cancellationToken),
            "Route change request retrieved successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>Decide an emergency route change request.</summary>
    /// <remarks>Manager only. Approval, rejection and return are atomic. Identical retries by the same Manager replay the original result without applying changes twice.</remarks>
    /// <param name="changeRequestId">The change request UUID.</param>
    /// <param name="request">Decision, lastUpdatedAt and instructions. Return requires a confirmed return backup or explicitly approved returnSegments.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the durable decision result.</returns>
    [HttpPost("{changeRequestId:guid}/decision")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Decide(Guid changeRequestId, [FromBody] Request.DecisionRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.DecideAsync(changeRequestId, AccountId(), request, cancellationToken),
            "Route change decision recorded successfully", traceId: HttpContext.TraceIdentifier));

    private Guid AccountId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedException("The authentication session is invalid or has expired.");
}
