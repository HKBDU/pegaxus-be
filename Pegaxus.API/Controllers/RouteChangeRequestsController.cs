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
    /// <summary>Request activation of a confirmed BACKUP route.</summary>
    /// <param name="routePlanId">The BACKUP route plan UUID.</param>
    /// <param name="request">Reason, new ETA and optional estimated cost.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the requested route.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/change-request")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Create(Guid routePlanId, [FromBody] Request.CreateRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.CreateAsync(routePlanId, AccountId(), request, cancellationToken),
            "Route change request sent successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>Approve or reject a BACKUP or RETURN route.</summary>
    /// <param name="routePlanId">The route plan UUID.</param>
    /// <param name="request">Decision, route version and optional note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the route and dossier status.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/change-decision")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Decide(Guid routePlanId, [FromBody] Request.DecisionRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.DecideAsync(routePlanId, AccountId(), request, cancellationToken),
            request.Decision == "APPROVE" ? "Route change approved successfully" : "Route change rejected successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>Activate a legally confirmed RETURN route.</summary>
    /// <param name="routePlanId">The RETURN route plan UUID.</param>
    /// <param name="request">Route version and optional resource assignments.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the active route segments.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/return-activation")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> ActivateReturn(Guid routePlanId, [FromBody] Request.ActivationRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.ActivateReturnAsync(routePlanId, AccountId(), request, cancellationToken),
            "Return route activated successfully", traceId: HttpContext.TraceIdentifier));

    /// <summary>Notify PRIMARY drivers about a confirmed or active RETURN route.</summary>
    /// <param name="routePlanId">The RETURN route plan UUID.</param>
    /// <param name="request">Optional driver message.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with notified drivers.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/driver-notice")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> NotifyDrivers(Guid routePlanId, [FromBody] Request.DriverNoticeRequest request, CancellationToken cancellationToken) =>
        Ok(ResponseBuilder.SuccessResponse(await service.NotifyDriversAsync(routePlanId, AccountId(), request, cancellationToken),
            "Driver notified successfully", traceId: HttpContext.TraceIdentifier));

    private Guid AccountId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedException("The authentication session is invalid or has expired.");
}
