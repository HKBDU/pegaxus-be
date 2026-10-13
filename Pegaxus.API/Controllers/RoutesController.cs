using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using Routes = Pegaxus.Service.RouteService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/routes")]
[Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
public sealed class RoutesController(Routes.IService service) : ApiControllerBase
{
    /// <summary>List active master routes.</summary>
    /// <param name="request">Location, mode, search, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A paginated route list.</returns>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Routes.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(request, cancellationToken), "Routes retrieved successfully");

    /// <summary>Get an active master route.</summary>
    /// <param name="routeId">The route identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The route details.</returns>
    [HttpGet("{routeId:guid}")]
    public async Task<IActionResult> Get(Guid routeId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(routeId, cancellationToken), "Route retrieved successfully");

    /// <summary>Create a master route.</summary>
    /// <param name="request">The route endpoints, mode, and note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created route.</returns>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Create([FromBody] Routes.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(AccountId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { routeId = result.RouteId }, ResponseBuilder.SuccessResponse(result, "Route created successfully", 201, HttpContext.TraceIdentifier));
    }

    /// <summary>Update a master route.</summary>
    /// <param name="routeId">The route identifier.</param>
    /// <param name="request">The route changes and concurrency token.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The updated route version.</returns>
    [HttpPut("{routeId:guid}")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Update(Guid routeId, [FromBody] Routes.Request.UpdateRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateAsync(AccountId, routeId, request, cancellationToken), "Route updated successfully");

    /// <summary>Soft-delete a master route.</summary>
    /// <param name="routeId">The route identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The deletion timestamp.</returns>
    [HttpDelete("{routeId:guid}")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Delete(Guid routeId, CancellationToken cancellationToken) =>
        Success(await service.DeleteAsync(AccountId, routeId, cancellationToken), "Route deleted successfully");

}
