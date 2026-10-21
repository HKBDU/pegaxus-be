using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Tracking = Pegaxus.Service.LocationTrackingService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/transports/{transportId}")]
public sealed class TransportLocationsController(Tracking.ILocationTrackingService service) : ApiControllerBase
{
    /// <summary>Get the transport tracking snapshot.</summary>
    /// <remarks>Managers, coordinators, specialists and the booking customer may view READY, IN_TRANSIT or COMPLETED transports. Returns the effective planned route, current stop, planned ETA and latest in-memory GPS sample. No GPS history is stored. Join /hubs/transport-tracking using JoinTransport(transportId), then load this snapshot on connection and reconnection. Subscribe to LocationUpdated for subsequent samples.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the tracking snapshot.</returns>
    [Authorize(Policy = JwtExtensions.TransportTrackingViewerPolicy)]
    [HttpGet("tracking")]
    public async Task<IActionResult> GetAsync(Guid transportId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(transportId, AccountId, cancellationToken), "Transport tracking retrieved successfully");

    /// <summary>Accept a driver GPS location.</summary>
    /// <remarks>Only assigned drivers may send positions while the transport is IN_TRANSIT, and only the driver of the segment in transit is tracked: samples from any other driver (the previous or the next one with the app still running) are acknowledged with 200 but ignored, so they are neither stored, streamed nor used for the geofence. The browser sends one current sample every 15 seconds. Coordinates remain in memory and are streamed through LocationUpdated; arrival timestamps are written to the database only when a started effective segment enters the configured geofence and the boundary after it needs no handover (the same driver runs the next segment); that call also starts the next segment. Boundaries that need a handover are never timed by GPS: the response carries an arrival object on every call inside the radius so the app can suggest creating the record. Optional routePlanRouteId must belong to the effective route. Duplicate or older samples are acknowledged without repeated effects. The device timestamp must not exceed server time by more than 5 minutes.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">Required latitude, longitude and recordedAt; optional speedKmh, headingDegrees, accuracyMeters and routePlanRouteId.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the current server time and, while the truck is inside the geofence of a segment end, an arrival object (null otherwise).</returns>
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [HttpPost("locations")]
    public async Task<IActionResult> RecordAsync(Guid transportId, [FromBody] Tracking.Request.LocationRequest request, CancellationToken cancellationToken) =>
        Success(await service.RecordAsync(transportId, request, AccountId, cancellationToken), "Location accepted");

    /// <summary>Confirm the arrival at the end of a segment that needs no handover.</summary>
    /// <remarks>Only the driver of the segment, while the transport is IN_TRANSIT. Allowed only when the same driver also runs the next segment; otherwise 422 HANDOVER_REQUIRED (the last segment always needs one). The coordinates only prove the truck is still within the geofence and are not stored. Overwrites the GPS arrival time with the server time and starts the next segment at the same moment. Once the truck has left the radius the GPS time is kept and the call returns 409 ARRIVAL_LOCKED; before any GPS time exists it returns 422 NOT_IN_ARRIVAL_RADIUS.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="routePlanRouteId">The segment that just ended (the segment before the boundary).</param>
    /// <param name="request">Required latitude and longitude of the truck.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the boundary number and the updated segments.</returns>
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [HttpPost("segments/{routePlanRouteId:guid}/arrival")]
    public async Task<IActionResult> ConfirmArrivalAsync(Guid transportId, Guid routePlanRouteId, [FromBody] Tracking.Request.ArrivalRequest request, CancellationToken cancellationToken) =>
        Success(await service.ConfirmArrivalAsync(transportId, routePlanRouteId, request, AccountId, cancellationToken), "Segment arrival confirmed successfully");

    /// <summary>Notify coordinators that a driver denied GPS permission.</summary>
    /// <remarks>Only assigned drivers may report denied location permission while the transport is IN_TRANSIT. Queues a HIGH priority notification for active assigned coordinators. Repeated calls from the same driver and transport within 10 minutes return notified=false. Cooldown and GPS samples belong to the running API process.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK indicating whether coordinators were notified.</returns>
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [HttpPost("locations/permission-denied")]
    public async Task<IActionResult> PermissionDeniedAsync(Guid transportId, CancellationToken cancellationToken) =>
        Success(await service.PermissionDeniedAsync(transportId, AccountId, cancellationToken), "GPS permission status recorded");
}
