using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Models;
using TransportPlanService = Pegaxus.Service.TransportPlanService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/bookings/{bookingId:guid}/transport-plans")]
public sealed class TransportPlanController(TransportPlanService.IService service) : ControllerBase
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
            GetAccountId(), transportPlanId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Transport plan updated successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Get transport plan details.</summary>
    /// <param name="transportPlanId">The transport plan identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the complete transport plan and booking context.</returns>
    [HttpGet("/api/v1/transport-plans/{transportPlanId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetById(Guid transportPlanId, CancellationToken cancellationToken)
    {
        var response = await service.GetByIdAsync(GetAccountId(), transportPlanId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Transport plan retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
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
        var response = await service.GetListAsync(GetAccountId(), request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Transport plans retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
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
            GetAccountId(), bookingId, request, cancellationToken);
        return CreatedAtAction(
            nameof(Create),
            new { bookingId },
            ResponseBuilder.SuccessResponse(
                response,
                "Transport plan created successfully",
                code: StatusCodes.Status201Created,
                traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirst("accountId")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var accountId)
            ? accountId
            : throw new InvalidOperationException("The authenticated account id is invalid.");
    }
}
