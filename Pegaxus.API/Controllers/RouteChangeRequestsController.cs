using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.RouteChangeService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/route-change-requests")]
[Authorize(Policy = JwtExtensions.LogisticsManagerOrFleetRouteCoordinatorPolicy)]
public sealed class RouteChangeRequestsController(IRouteChangeService service) : ApiControllerBase
{
    /// <summary>Request activation of a confirmed BACKUP route.</summary>
    /// <param name="routePlanId">The BACKUP route plan UUID.</param>
    /// <param name="request">Reason, new ETA, optional estimated cost and optional incidentReportId (an open incident on the same transport moves to PENDING).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the requested route.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/change-request")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Create(Guid routePlanId, [FromBody] Request.CreateRequest request, CancellationToken cancellationToken) =>
        Success(await service.CreateAsync(routePlanId, AccountId, request, cancellationToken), "Route change request sent successfully");

    /// <summary>Read the latest request to activate a BACKUP route.</summary>
    /// <remarks>
    /// What the Route Coordinator sent: who and when, reason, new ETA, estimated extra cost, note, linked incident, and how big the
    /// change is in plain words. Data is null when no request was sent. The Logistics Manager, or the Route Coordinator assigned to
    /// the transport plan.
    /// </remarks>
    /// <param name="routePlanId">The BACKUP route plan UUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the latest request, or null data when there is none.</returns>
    [HttpGet("/api/v1/route-plans/{routePlanId:guid}/change-request")]
    public async Task<IActionResult> GetChangeRequest(Guid routePlanId, CancellationToken cancellationToken) =>
        Success(await service.GetChangeRequestAsync(routePlanId, AccountId, cancellationToken), "Route change request retrieved successfully");

    /// <summary>Suggest the crew of a confirmed BACKUP route before it is approved.</summary>
    /// <remarks>
    /// Lists the backup segments from the branch point. Internal segments take the driver and escort of the untravelled PRIMARY
    /// segments by position; needsCrew marks a segment still missing someone. suggestedStartAt is the planned departure, or now when
    /// that has passed. The Logistics Manager, or the Route Coordinator assigned to the transport plan.
    /// </remarks>
    /// <param name="routePlanId">The BACKUP route plan UUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the suggested crew; 409 when the transport is not in transit or the route is not a confirmed backup.</returns>
    [HttpGet("/api/v1/route-plans/{routePlanId:guid}/change-crew")]
    public async Task<IActionResult> GetCrewPlan(Guid routePlanId, CancellationToken cancellationToken) =>
        Success(await service.GetCrewPlanAsync(routePlanId, AccountId, cancellationToken), "Backup crew suggestion retrieved successfully");

    /// <summary>Approve or reject a BACKUP or RETURN route.</summary>
    /// <remarks>
    /// For a BACKUP approval, startAt moves every backup segment so the first one departs then, and segments overrides the suggested
    /// driver, escort or vehicles of internal backup segments (an empty field keeps the suggestion). The segment type cannot change.
    /// </remarks>
    /// <param name="routePlanId">The route plan UUID.</param>
    /// <param name="request">Decision, route version, optional note, optional incidentReportId (an open incident on the same transport moves to PENDING), and for a BACKUP approval optional startAt and segments.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the route and dossier status.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/change-decision")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Decide(Guid routePlanId, [FromBody] Request.DecisionRequest request, CancellationToken cancellationToken) =>
        Success(await service.DecideAsync(routePlanId, AccountId, request, cancellationToken), request.Decision == "APPROVE" ? "Route change approved successfully" : "Route change rejected successfully");

    /// <summary>Suggest the crew of a confirmed RETURN route before it is activated.</summary>
    /// <remarks>
    /// Internal return segments take the crew at the current stop (the crew of the next segment, or else the crew that brought the
    /// horse there); needsCrew marks a segment still missing someone. Fails with 409 SEGMENT_LOCKED while the horse is between two
    /// stops and 422 RETURN_NOT_ANCHORED when the return does not leave from the current stop.
    /// </remarks>
    /// <param name="routePlanId">The RETURN route plan UUID.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the suggested crew and start time.</returns>
    [HttpGet("/api/v1/route-plans/{routePlanId:guid}/return-crew")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetReturnCrewPlan(Guid routePlanId, CancellationToken cancellationToken) =>
        Success(await service.GetReturnCrewPlanAsync(routePlanId, AccountId, cancellationToken), "Return crew suggestion retrieved successfully");

    /// <summary>Activate a legally confirmed RETURN route.</summary>
    /// <remarks>
    /// startAt moves every return segment so the first one departs then. Each segment entry can replace the suggested driver and
    /// escort, the vehicles, or switch to a transport provider.
    /// </remarks>
    /// <param name="routePlanId">The RETURN route plan UUID.</param>
    /// <param name="request">Route version, optional start time and optional crew and resource assignments.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the active route segments.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/return-activation")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> ActivateReturn(Guid routePlanId, [FromBody] Request.ActivationRequest request, CancellationToken cancellationToken) =>
        Success(await service.ActivateReturnAsync(routePlanId, AccountId, request, cancellationToken), "Return route activated successfully");

    /// <summary>Notify PRIMARY drivers about a confirmed or active RETURN route.</summary>
    /// <remarks>The RC assigned to the transport plan, or any LM when the RC is absent.</remarks>
    /// <param name="routePlanId">The RETURN route plan UUID.</param>
    /// <param name="request">Optional driver message.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with notified drivers.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/driver-notice")]
    public async Task<IActionResult> NotifyDrivers(Guid routePlanId, [FromBody] Request.DriverNoticeRequest request, CancellationToken cancellationToken) =>
        Success(await service.NotifyDriversAsync(routePlanId, AccountId, request, cancellationToken), "Driver notified successfully");

}
