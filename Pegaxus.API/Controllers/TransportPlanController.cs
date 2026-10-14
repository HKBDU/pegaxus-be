using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using TransportPlanService = Pegaxus.Service.TransportPlanService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/bookings/{bookingId:guid}/transport-plans")]
public sealed class TransportPlanController(TransportPlanService.ITransportPlanService service) : ApiControllerBase
{
    /// <summary>Update transport plan metadata.</summary>
    /// <param name="transportPlanId">The transport plan identifier (UUID).</param>
    /// <param name="request">The concurrency token and metadata changes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated transport plan.</returns>
    [HttpPut("/api/v1/transport-plans/{transportPlanId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Update(
        Guid transportPlanId,
        [FromBody] TransportPlanService.Request.UpdateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.UpdateAsync(
            AccountId, transportPlanId, request, cancellationToken);
        return Success(response, "Transport plan updated successfully");
    }

    /// <summary>Get transport plan details.</summary>
    /// <param name="transportPlanId">The transport plan identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the complete transport plan and booking context.</returns>
    [HttpGet("/api/v1/transport-plans/{transportPlanId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetById(Guid transportPlanId, CancellationToken cancellationToken)
    {
        var response = await service.GetByIdAsync(AccountId, transportPlanId, cancellationToken);
        return Success(response, "Transport plan retrieved successfully");
    }

    /// <summary>List transport plans.</summary>
    /// <param name="request">The status, search, route status, sorting, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated transport plan list.</returns>
    [HttpGet("/api/v1/transport-plans")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetList(
        [FromQuery] TransportPlanService.Request.ListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.GetListAsync(AccountId, request, cancellationToken);
        return Success(response, "Transport plans retrieved successfully");
    }

    /// <summary>Create an overall transport plan.</summary>
    /// <param name="bookingId">The approved booking identifier (UUID).</param>
    /// <param name="request">The transport plan schedule and estimate metadata.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the draft transport plan and updated transport.</returns>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Create(
        Guid bookingId,
        [FromBody] TransportPlanService.Request.CreateRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(
            AccountId, bookingId, request, cancellationToken);
        return CreatedAtAction(
            nameof(Create),
            new { bookingId },
            ResponseBuilder.SuccessResponse(
                response,
                "Transport plan created successfully",
                code: StatusCodes.Status201Created,
                traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Assign the first-phase Transport Specialist and Fleet Route Coordinator.</summary>
    /// <param name="transportPlanId">The transport plan identifier.</param>
    /// <param name="request">The phase 1 assignment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the assignments.</returns>
    [HttpPost("/api/v1/transport-plans/{transportPlanId:guid}/assignments/phase-1")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> AssignPhase1(
        Guid transportPlanId,
        [FromBody] TransportPlanService.Request.Phase1AssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.AssignPhase1Async(
            AccountId, transportPlanId, request, cancellationToken);
        return Created(
            $"/api/v1/transport-plans/{transportPlanId}",
            ResponseBuilder.SuccessResponse(
                response,
                "Phase 1 personnel assigned successfully",
                code: StatusCodes.Status201Created,
                traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Replace one or both first-phase personnel assignments.</summary>
    /// <param name="transportPlanId">The transport plan identifier.</param>
    /// <param name="request">The replacement assignment.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the assignments.</returns>
    [HttpPut("/api/v1/transport-plans/{transportPlanId:guid}/assignments/phase-1")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> UpdatePhase1(
        Guid transportPlanId,
        [FromBody] TransportPlanService.Request.UpdatePhase1AssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.UpdatePhase1Async(
            AccountId, transportPlanId, request, cancellationToken);
        return Success(response, "Phase 1 personnel updated successfully");
    }

}
