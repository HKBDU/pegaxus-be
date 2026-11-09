using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using RoutePlanService = Pegaxus.Service.RoutePlanService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
public sealed class RoutePlansController(RoutePlanService.IRoutePlanService service) : ApiControllerBase
{
    /// <summary>Create a route plan.</summary>
    /// <param name="transportPlanId">The transport plan identifier.</param>
    /// <param name="request">The route plan data. RETURN may be created by the assigned RC or any LM and may carry the incident that caused it (incidentReportId).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the route plan.</returns>
    [HttpPost("/api/v1/transport-plans/{transportPlanId:guid}/route-plans")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> Create(Guid transportPlanId, [FromBody] RoutePlanService.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(AccountId, transportPlanId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { routePlanId = response.RoutePlanId }, ResponseBuilder.SuccessResponse(response, "Route plan created successfully", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

    /// <summary>Get the reversed route of the original trip, for the return booking of a horse left behind.</summary>
    /// <remarks>Only for the transport plan of a return booking (NOT_RETURN_BOOKING otherwise). Returns the legs the original transport drove
    /// up to the stop where the horse stays, reversed, without times, vehicles or staff. Fill them in and save them with the create endpoint.</remarks>
    /// <param name="transportPlanId">The transport plan of the return booking.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the reversed legs (empty when the horse stayed at the origin).</returns>
    [HttpGet("/api/v1/transport-plans/{transportPlanId:guid}/return-route-template")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> GetReturnTemplate(Guid transportPlanId, CancellationToken cancellationToken) =>
        Success(await service.GetReturnTemplateAsync(AccountId, transportPlanId, cancellationToken), "Return route template retrieved successfully");

    /// <summary>List route plans for a transport plan.</summary>
    /// <param name="transportPlanId">The transport plan identifier.</param>
    /// <param name="request">The route plan filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with route plan summaries.</returns>
    [HttpGet("/api/v1/transport-plans/{transportPlanId:guid}/route-plans")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetList(Guid transportPlanId, [FromQuery] RoutePlanService.Request.ListRequest request, CancellationToken cancellationToken)
    {
        var response = await service.GetListAsync(AccountId, transportPlanId, request, cancellationToken);
        return Success(response, "Route plans retrieved successfully");
    }

    /// <summary>Get a route plan.</summary>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with route plan details.</returns>
    [HttpGet("/api/v1/route-plans/{routePlanId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetById(Guid routePlanId, CancellationToken cancellationToken)
    {
        var response = await service.GetByIdAsync(AccountId, routePlanId, cancellationToken);
        return Success(response, "Route plan retrieved successfully");
    }

    /// <summary>Update a draft route plan.</summary>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="request">The route plan changes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated route plan summary.</returns>
    [HttpPut("/api/v1/route-plans/{routePlanId:guid}")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> Update(Guid routePlanId, [FromBody] RoutePlanService.Request.UpdateRequest request, CancellationToken cancellationToken)
    {
        var response = await service.UpdateAsync(AccountId, routePlanId, request, cancellationToken);
        return Success(response, "Route plan updated successfully");
    }

    /// <summary>Change the vehicles of a route plan, before departure or after an incident during the trip.</summary>
    /// <remarks>Only the Route Coordinator can use this. While the PRIMARY route plan is CONFIRMED (legal review passed, PICKUP not yet completed) segments are picked by sequenceNo or routePlanRouteId. While the route plan is ACTIVE and the transport IN_TRANSIT, incidentReportId is required, segments are picked by routePlanRouteId and may belong to any unfinished segment of the transport (the current one, later ones, and the segments of a CONFIRMED BACKUP or RETURN plan); the incident must report each replaced vehicle and have an APPROVED REPLACEMENT_VEHICLE emergency cost. Each listed segment replaces its vehicles; the same replacement is also applied to the other unfinished segments of the transport plan that still hold the replaced vehicle (for example a CONFIRMED BACKUP or RETURN plan), unless the request lists them itself, and the whole call fails if any of them is invalid (AMBIGUOUS_REPLACEMENT when one vehicle gets different replacements and a segment that still uses it is not listed). While ACTIVE, a new vehicle that is IN_USE on another trip is refused. The new vehicles pass the same checks as when planning (status, capacity, documents, schedule). A replaced vehicle that nothing else holds returns to AVAILABLE, or goes to MAINTENANCE when the incident needs repair. Errors include INVALID_ROUTE_PLAN_STATUS, INVALID_ROUTE_PLAN_TYPE, ROUTE_PLAN_MODIFIED, TRANSPORT_NOT_IN_TRANSIT, INCIDENT_REQUIRED, INCIDENT_NOT_FOUND, INVALID_INCIDENT_STATUS, VEHICLE_NOT_IN_INCIDENT, REPLACEMENT_COST_NOT_APPROVED, SEGMENT_ALREADY_COMPLETED, SEGMENT_NOT_IN_ITINERARY, VEHICLE_UNAVAILABLE and VEHICLE_SCHEDULE_CONFLICT.</remarks>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="request">The reason, the concurrency token, the incident (while ACTIVE) and the new vehicles per segment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the route plan status and the vehicles that went back to the pool.</returns>
    [HttpPatch("/api/v1/route-plans/{routePlanId:guid}/vehicles")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> ChangeVehicles(Guid routePlanId, [FromBody] RoutePlanService.Request.ChangeVehiclesRequest request, CancellationToken cancellationToken)
    {
        var response = await service.ChangeVehiclesAsync(AccountId, routePlanId, request, cancellationToken);
        return Success(response, "Route plan vehicles changed successfully");
    }

    /// <summary>Cancel a draft route plan.</summary>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="request">The cancellation reason and concurrency token.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled route plan.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/cancel")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> Cancel(Guid routePlanId, [FromBody] RoutePlanService.Request.CancelRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CancelAsync(AccountId, routePlanId, request, cancellationToken);
        return Success(response, "Route plan cancelled successfully");
    }

    /// <summary>Export a route plan as PDF.</summary>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the PDF file.</returns>
    [HttpGet("/api/v1/route-plans/{routePlanId:guid}/export")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Export(Guid routePlanId, CancellationToken cancellationToken)
    {
        var response = await service.ExportAsync(AccountId, routePlanId, Request.Query["format"].FirstOrDefault(), cancellationToken);
        return File(response.Content, response.ContentType, response.FileName);
    }

    /// <summary>Review the legal feasibility of a route plan.</summary>
    /// <param name="routePlanId">The route plan identifier.</param>
    /// <param name="request">The legal review result.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the legal review result.</returns>
    [HttpPost("/api/v1/route-plans/{routePlanId:guid}/legal-review")]
    [Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
    public async Task<IActionResult> ReviewLegal(Guid routePlanId, [FromBody] RoutePlanService.Request.LegalReviewRequest request, CancellationToken cancellationToken)
    {
        var response = await service.ReviewLegalAsync(AccountId, routePlanId, request, cancellationToken);
        return Success(response, response.LegalReview.IsFeasible ? "Route plan confirmed successfully" : "Route plan requires revision");
    }

}
