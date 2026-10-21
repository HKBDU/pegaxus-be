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
    /// <summary>List incidents visible to the current employee.</summary>
    /// <remarks>IN7: LM, RC and TS see every incident. Driver/Escort see their reports and assigned transports. Counts cover the visible scope before filters. Reads never accept or change incidents.</remarks>
    /// <param name="request">Filters, date range, search, sorting and pagination.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with counts and a page of incidents.</returns>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.IncidentListViewerPolicy)]
    public async Task<IActionResult> ListAsync([FromQuery] Incidents.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.ListAsync(request, AccountId, cancellationToken), "Incidents retrieved successfully");

    /// <summary>Read an incident without changing its status.</summary>
    /// <remarks>IN8: employees follow IN7 visibility. Customers may read HIGH/CRITICAL incidents belonging to their bookings; financial fields, action controls and phone numbers are omitted. Incidents outside the caller's scope return 404.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the role-specific incident detail.</returns>
    [HttpGet("{incidentReportId}")]
    [Authorize(Policy = JwtExtensions.IncidentViewerPolicy)]
    public async Task<IActionResult> GetAsync(Guid incidentReportId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(incidentReportId, AccountId, cancellationToken), "Incident retrieved successfully");

    /// <summary>Read the incident's emergency rerouting options.</summary>
    /// <remarks>RT1: LM or assigned RC. Returns the effective itinerary, BACKUP/RETURN plans, static driving/rest checks, dossiers and hold. Closed incidents remain readable with no actions. GPS positions come from Flow 4.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the rerouting context. This read never changes incident or route state.</returns>
    [HttpGet("{incidentReportId}/reroute-context")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetRerouteContextAsync(Guid incidentReportId, CancellationToken cancellationToken) =>
        Success(await service.GetRerouteContextAsync(incidentReportId, AccountId, cancellationToken), "Reroute context retrieved successfully");

    /// <summary>Escalate blocked routes to the logistics manager.</summary>
    /// <remarks>RT2: assigned RC, open incident, IN_TRANSIT only. Accepts REPORTED as PENDING and queues urgent ROUTE alerts to active managers and assigned TS, plus manager email. Suggestions never create or activate a route.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">Reason, optional HOLD/RETURN suggestion and blocked route plan identifiers.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with notified managers and send time; 422 for a closed incident.</returns>
    [HttpPost("{incidentReportId}/reroute-escalation")]
    [Authorize(Policy = JwtExtensions.FleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> EscalateRerouteAsync(Guid incidentReportId, [FromBody] Incidents.Request.RerouteEscalationRequest request, CancellationToken cancellationToken) =>
        Success(await service.EscalateRerouteAsync(incidentReportId, request, AccountId, cancellationToken), "Escalation sent to the logistics manager");

    /// <summary>Order the crew to wait at a safe stop.</summary>
    /// <remarks>RT3: LM, open incident, IN_TRANSIT only. Records a 15-240 minute hold and a 10-120 minute monitoring interval (default 30). Notifies current crew, assigned RC and TS. A transport cannot have two active holds. Expiry alerts staff without changing the transport, route, vehicle or medical history.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">Hold duration, reason, monitoring interval and optional safe-stop location.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the hold and notified employees; 409 HOLD_ALREADY_ACTIVE for an existing hold.</returns>
    [HttpPost("{incidentReportId}/route-hold")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> HoldRouteAsync(Guid incidentReportId, [FromBody] Incidents.Request.RouteHoldRequest request, CancellationToken cancellationToken) =>
        Success(await service.HoldRouteAsync(incidentReportId, request, AccountId, cancellationToken), "Transport put on hold successfully");

    /// <summary>Confirm emergency settlement and resolve an incident.</summary>
    /// <remarks>IN9: LM only, REPORTED/PENDING, transport IN_TRANSIT or COMPLETED. Pending cost requests block resolution with value.pendingRequests. Confirmation and a summary of at least 20 trimmed characters are required. Vehicle availability and medical history are preserved.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">The incident version, settlement confirmation and resolution summary.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the resolved incident; 409 for a stale version, 422 for pending costs.</returns>
    [HttpPost("{incidentReportId}/resolve")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> ResolveAsync(Guid incidentReportId, [FromBody] Incidents.Request.ResolveRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ResolveAsync(incidentReportId, request, AccountId, cancellationToken);
        return Success(result, $"Incident {result.IncidentCode} resolved. Approved emergency costs are ready for the financial report.");
    }

    /// <summary>Cancel an incorrectly reported incident.</summary>
    /// <remarks>IN10: LM only, REPORTED/PENDING. Approved costs block cancellation. Pending costs are cancelled atomically with the same reason. The settlement flag, vehicle availability and medical history are preserved.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">The incident version and a cancellation reason of at least 10 trimmed characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled incident; 422 with value.approvedRequests for approved costs.</returns>
    [HttpPost("{incidentReportId}/cancel")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> CancelAsync(Guid incidentReportId, [FromBody] Incidents.Request.CancelRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CancelAsync(incidentReportId, request, AccountId, cancellationToken);
        return Success(result, $"Incident {result.IncidentCode} cancelled");
    }

    /// <summary>Let the assigned crew resolve a reported incident without open costs.</summary>
    /// <remarks>IN11: assigned Driver/Escort, REPORTED and IN_TRANSIT only. PENDING/APPROVED costs require LM resolution. Severity, vehicle availability and medical history are preserved.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">The incident version and a resolution summary of at least 20 trimmed characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the resolved incident; 422 with value.requests for open costs.</returns>
    [HttpPost("{incidentReportId}/self-resolve")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    public async Task<IActionResult> SelfResolveAsync(Guid incidentReportId, [FromBody] Incidents.Request.SelfResolveRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SelfResolveAsync(incidentReportId, request, AccountId, cancellationToken);
        return Success(result, $"Incident {result.IncidentCode} resolved");
    }

    /// <summary>Raise the severity of an open incident.</summary>
    /// <remarks>IN12: LM or assigned RC/Driver/Escort. Severity only increases; reason appends with author and UTC time. HIGH/CRITICAL queue urgent staff alerts, manager email and one sanitized customer notification per event. Status remains unchanged.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">The incident version, a higher severity and a reason of at least 10 trimmed characters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the incident and notification effects; 422 for equal or decreased severity.</returns>
    [HttpPost("{incidentReportId}/raise-severity")]
    [Authorize(Policy = JwtExtensions.IncidentOperatorPolicy)]
    public async Task<IActionResult> RaiseSeverityAsync(Guid incidentReportId, [FromBody] Incidents.Request.RaiseSeverityRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RaiseSeverityAsync(incidentReportId, request, AccountId, cancellationToken);
        return Success(result.Value, $"Incident {result.Value.IncidentCode} severity raised from {result.PreviousSeverity} to {result.Value.Severity}");
    }

    /// <summary>Report a vehicle or combined vehicle and horse incident.</summary>
    /// <remarks>IN1: Only an assigned Driver or Escort may report while IN_TRANSIT. Upload up to five JPG/PNG images (5MB each) through uploads/signature with entity attachment, then pass their publicIds in photoFiles. BOTH forces CRITICAL. A non-operable vehicle requiring replacement becomes OUT_OF_SERVICE. Alerts and urgent manager email are queued atomically. Replays with the same transport, reporter, vehicle, incident type and occurredAt return the original report without repeated effects.</remarks>
    /// <param name="request">The incident, vehicle, location and optional uploaded photos.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the incident, effects and safety contacts; 200 OK for an offline replay.</returns>
    [HttpPost("vehicle")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
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
    public async Task<IActionResult> UpdateVehicleAsync(Guid incidentReportId, [FromBody] Incidents.Request.UpdateVehicleRequest request, CancellationToken cancellationToken) =>
        Success(await service.UpdateVehicleAsync(incidentReportId, request, AccountId, cancellationToken), "Vehicle incident details updated successfully");

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
