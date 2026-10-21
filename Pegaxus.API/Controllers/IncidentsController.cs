using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/incidents")]
public sealed class IncidentsController(Incidents.IIncidentService service) : ApiControllerBase
{
    /// <summary>Report a vehicle or combined vehicle and horse incident.</summary>
    /// <remarks>IN1: Only an assigned Driver or Escort may report while IN_TRANSIT. Upload up to five JPG/PNG images (5MB each) through uploads/signature with entity attachment, then pass their publicIds in photoFiles. BOTH forces CRITICAL. A non-operable vehicle requiring replacement becomes OUT_OF_SERVICE. Alerts and urgent manager email are queued atomically. Replays with the same transport, reporter, vehicle, incident type and occurredAt return the original report without repeated effects.</remarks>
    /// <param name="request">The incident, vehicle, location and optional uploaded photos.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the incident, effects and safety contacts; 200 OK for an offline replay.</returns>
    [HttpPost("vehicle")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    [Consumes("application/json")]
    public async Task<IActionResult> ReportVehicleAsync([FromBody] Incidents.Request.ReportVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ReportVehicleAsync(request, AccountId, cancellationToken);
        var message = $"Vehicle incident {result.Value.IncidentCode} reported successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }

    /// <summary>Supplement an open vehicle incident with details and photos.</summary>
    /// <remarks>IN4: Logistics Managers, the assigned Coordinator, or the assigned original Driver/Escort reporter may update REPORTED/PENDING incidents, including after the transport completes. lastUpdatedAt must match the incident. At least one vehicle field besides photoFiles is required. Updates notify internal staff at NORMAL priority and never restore a vehicle's availability.</remarks>
    /// <param name="incidentReportId">The incident identifier (UUID).</param>
    /// <param name="request">The lastUpdatedAt version, changed vehicle fields, and up to five uploaded photos.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the full incident; 409 for a stale version; 422 for a closed or incompatible incident.</returns>
    [HttpPatch("{incidentReportId}/vehicle-detail")]
    [Authorize(Policy = JwtExtensions.VehicleIncidentOperatorPolicy)]
    [Consumes("application/json")]
    public async Task<IActionResult> UpdateVehicleAsync(Guid incidentReportId, [FromBody] Incidents.Request.UpdateVehicleRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateVehicleAsync(incidentReportId, request, AccountId, cancellationToken), "Vehicle incident details updated successfully");
}
