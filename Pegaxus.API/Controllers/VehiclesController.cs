using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

using Vehicles = Pegaxus.Service.VehicleService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/vehicles")]
[Authorize(Policy = JwtExtensions.VehicleManagerPolicy)]
public sealed class VehiclesController(Vehicles.IVehicleService service) : ApiControllerBase
{
    /// <summary>List specialized vehicles.</summary>
    /// <remarks>Lists undeleted ROAD, SEA and AIR vehicles. Optional date filters return vehicles without unfinished schedule conflicts.</remarks>
    /// <param name="request">Pagination, search, status, capacity, dates, operating country (2-letter ISO code), transportPlanId (keeps vehicles already assigned to that plan when filtering by availability), and sorting.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated vehicle catalog and expiry warnings.</returns>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] Vehicles.Request.ListRequest request, CancellationToken cancellationToken)
        => Success(await service.GetListAsync(request, AccountId, cancellationToken), "Vehicles retrieved successfully");

    /// <summary>Get a specialized vehicle.</summary>
    /// <remarks>Includes expiry warnings and upcoming or ongoing unfinished route assignments.</remarks>
    /// <param name="vehicleId">The vehicle identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with vehicle details, or 404 VEHICLE_NOT_FOUND.</returns>
    [HttpGet("{vehicleId:guid}")]
    public async Task<IActionResult> Get(Guid vehicleId, CancellationToken cancellationToken)
        => Success(await service.GetAsync(vehicleId, AccountId, cancellationToken), "Vehicle retrieved successfully");

    /// <summary>Create a specialized vehicle.</summary>
    /// <remarks>Creates an AVAILABLE vehicle with a unique license plate and capacity between 2 and 9. The server generates the vehicle code (VEH-yyyyMMdd-0001); sending vehicleCode is rejected.</remarks>
    /// <param name="request">The editable technical and document fields. The vehicle code is generated and returned in the response.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with vehicle details, or field errors and duplicate conflicts.</returns>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Vehicles.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, AccountId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { vehicleId = result.VehicleId }, ResponseBuilder.SuccessResponse(result,
            $"Vehicle {result.VehicleCode} ({result.LicensePlate}) created successfully", 201, HttpContext.TraceIdentifier));
    }

    /// <summary>Update specialized vehicle information.</summary>
    /// <remarks>Replaces editable fields. Vehicle identifier, code and status are read-only. Mode changes require no unfinished assignments. Stale versions return 409 VEHICLE_MODIFIED.</remarks>
    /// <param name="vehicleId">The vehicle identifier (UUID).</param>
    /// <param name="request">Replacement vehicle fields and the last observed updatedAt.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the new version and document warnings.</returns>
    [HttpPut("{vehicleId:guid}")]
    public async Task<IActionResult> Update(Guid vehicleId, [FromBody] Vehicles.Request.UpdateRequest request, CancellationToken cancellationToken)
        => Success(await service.UpdateAsync(vehicleId, request, AccountId, cancellationToken), "Vehicle updated successfully");

    /// <summary>Update a specialized vehicle status.</summary>
    /// <remarks>Allows AVAILABLE, MAINTENANCE or INACTIVE with a reason. Assigned vehicles are locked. The change, audit, and notifications commit together.</remarks>
    /// <param name="vehicleId">The vehicle identifier (UUID).</param>
    /// <param name="request">The manual status, reason, and last observed updatedAt.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with previous status and new version, or 409 for locked or modified vehicles.</returns>
    [HttpPatch("{vehicleId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid vehicleId, [FromBody] Vehicles.Request.StatusRequest request, CancellationToken cancellationToken)
        => Success(await service.ChangeStatusAsync(vehicleId, request, AccountId, cancellationToken), "Vehicle status updated successfully");

}
