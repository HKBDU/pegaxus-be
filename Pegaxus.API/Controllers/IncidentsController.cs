using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/incidents")]
[Consumes("application/json")]
public sealed class IncidentsController(Incidents.IIncidentService service) : ApiControllerBase
{
    /// <summary>Report a health incident affecting one or more booked horses.</summary>
    /// <remarks>IN2: assigned Driver or Escort, IN_TRANSIT only. Creates one medical detail and immutable INCIDENT_FOLLOWUP log per horse. Urgent staff alerts and manager email are queued atomically. Offline replays return the original report without repeated effects. Photos use confirmed presigned publicIds.</remarks>
    /// <param name="request">Severity, horses, location, optional occurrence time and up to five photos.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created for a new incident, or 200 OK for an offline replay.</returns>
    [HttpPost("horse-health")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    public async Task<IActionResult> ReportHorseHealthAsync([FromBody] Incidents.Request.ReportHorseHealthRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ReportHorseHealthAsync(request, AccountId, cancellationToken);
        var message = $"Horse health incident {result.Value.IncidentCode} reported successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }

    /// <summary>Add previously unreported horses to an open horse or combined incident.</summary>
    /// <remarks>IN3: assigned Escort only. Each horse can have one detail per incident. Adds immutable health logs, photos and urgent staff alerts atomically without changing severity. Available after transport completion while the incident remains open.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">New horse details and up to five optional presigned photo references.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the incident and creation effects.</returns>
    [HttpPost("{incidentReportId}/horse-details")]
    [Authorize(Policy = JwtExtensions.EscortPolicy)]
    public async Task<IActionResult> AddHorseDetailsAsync(Guid incidentReportId, [FromBody] Incidents.Request.AddHorseDetailsRequest request, CancellationToken cancellationToken) =>
        SuccessCreated(await service.AddHorseDetailsAsync(incidentReportId, request, AccountId, cancellationToken), "Horse incident details added successfully");

    /// <summary>Append veterinary follow-up to an existing horse incident detail.</summary>
    /// <remarks>IN5: Logistics Manager, assigned coordinator or Escort, or assigned reporting Driver. Original medical fields are immutable. Notes append with UTC time and author; veterinarian contact time can be set once. horseTransportable supports true, false and explicit null. lastUpdatedAt is the incident version, not the detail version.</remarks>
    /// <param name="incidentReportId">The open incident identifier.</param>
    /// <param name="horseHealthIncidentDetailId">The medical detail belonging to this incident.</param>
    /// <param name="request">Incident lastUpdatedAt and at least one follow-up field.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the incident; 409 for a stale version, 422 for medical edits.</returns>
    [HttpPatch("{incidentReportId}/horse-details/{horseHealthIncidentDetailId}")]
    [Authorize(Policy = JwtExtensions.IncidentOperatorPolicy)]
    public async Task<IActionResult> FollowUpHorseAsync(Guid incidentReportId, Guid horseHealthIncidentDetailId,
        [FromBody] Incidents.Request.FollowUpHorseRequest request, CancellationToken cancellationToken) =>
        Success(await service.FollowUpHorseAsync(incidentReportId, horseHealthIncidentDetailId, request, AccountId, cancellationToken), "Horse incident follow-up added successfully");
}
