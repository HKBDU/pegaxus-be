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
    /// <remarks>Only assigned drivers may send positions while the transport is IN_TRANSIT. The browser sends one current sample every 15 seconds. Coordinates remain in memory and are streamed through LocationUpdated; only arrival timestamps are written to the database when a started effective segment enters the configured geofence. Never starts a segment. Optional routePlanRouteId must belong to the effective route. Duplicate or older samples are acknowledged without repeated effects. The device timestamp must not exceed server time by more than 5 minutes.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">Required latitude, longitude and recordedAt; optional speedKmh, headingDegrees, accuracyMeters and routePlanRouteId.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the current server time.</returns>
    [Authorize(Policy = JwtExtensions.DriverPolicy)]
    [HttpPost("locations")]
    public async Task<IActionResult> RecordAsync(Guid transportId, [FromBody] Tracking.Request.LocationRequest request, CancellationToken cancellationToken) =>
        Success(await service.RecordAsync(transportId, request, AccountId, cancellationToken), "Location accepted");

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
