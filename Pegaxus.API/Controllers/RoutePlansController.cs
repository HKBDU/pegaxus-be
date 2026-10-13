using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using RoutePlanService = Pegaxus.Service.RoutePlanService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
public sealed class RoutePlansController(RoutePlanService.IService service) : ApiControllerBase
{
    /// <summary>Create a route plan.</summary>
    /// <param name="transportPlanId">The transport plan identifier.</param>
    /// <param name="request">The route plan data.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the route plan.</returns>
    [HttpPost("/api/v1/transport-plans/{transportPlanId:guid}/route-plans")]
    [Authorize(Policy = JwtExtensions.EmployeePolicy)]
    public async Task<IActionResult> Create(Guid transportPlanId, [FromBody] RoutePlanService.Request.SaveRequest request, CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(AccountId, transportPlanId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { routePlanId = response.RoutePlanId }, ResponseBuilder.SuccessResponse(response, "Route plan created successfully", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }

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
