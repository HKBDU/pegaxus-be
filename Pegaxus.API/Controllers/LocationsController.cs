using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using Locations = Pegaxus.Service.LocationService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/locations")]
[Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
public sealed class LocationsController(Locations.ILocationService service) : ApiControllerBase
{
    /// <summary>List active master locations.</summary>
    /// <remarks>Supply both latitude and longitude to find locations within 500 meters, nearest first. GPS coordinates may have more than four decimal places. No match returns an empty list without creating a location. Without coordinates, locations remain ordered by name.</remarks>
    /// <param name="request">Search, filters, pagination and an optional GPS coordinate pair.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A paginated location list.</returns>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Locations.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(request, cancellationToken), "Locations retrieved successfully");

    /// <summary>Get an active master location.</summary>
    /// <param name="locationId">The location identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The location details.</returns>
    [HttpGet("{locationId:guid}")]
    public async Task<IActionResult> Get(Guid locationId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(locationId, cancellationToken), "Location retrieved successfully");

    /// <summary>Create a master location.</summary>
    /// <param name="request">The location details and coordinates.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created location.</returns>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Create([FromBody] Locations.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(AccountId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { locationId = result.LocationId }, ResponseBuilder.SuccessResponse(result, "Location created successfully", 201, HttpContext.TraceIdentifier));
    }

    /// <summary>Update a master location.</summary>
    /// <param name="locationId">The location identifier.</param>
    /// <param name="request">The updated location details and concurrency token.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The updated location version.</returns>
    [HttpPut("{locationId:guid}")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Update(Guid locationId, [FromBody] Locations.Request.UpdateRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateAsync(AccountId, locationId, request, cancellationToken), "Location updated successfully");

    /// <summary>Soft-delete a master location.</summary>
    /// <param name="locationId">The location identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The deletion timestamp.</returns>
    [HttpDelete("{locationId:guid}")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> Delete(Guid locationId, CancellationToken cancellationToken) =>
        Success(await service.DeleteAsync(AccountId, locationId, cancellationToken), "Location deleted successfully");

}
