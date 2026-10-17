using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using HorseHealthLogs = Pegaxus.Service.HorseHealthLogService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.EscortPolicy)]
[Route("api/v1/transports/{transportId}/horse-health-logs")]
public sealed class TransportHorseHealthLogsController(HorseHealthLogs.IHorseHealthLogService service) : ApiControllerBase
{
    /// <summary>Record a horse health check and up to five optional photos.</summary>
    /// <remarks>Only the assigned Escort may record checks while the transport is READY or IN_TRANSIT. Upload photos through /api/v1/uploads/signature first, then submit their publicIds in files. Critical checks or veterinarian requests notify managers and assigned coordinators atomically. A repeated transport, horse, Escort, log type, and checkedAt returns the existing immutable check with no repeated effects. GPS and photo metadata are ignored.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The horse, health check fields, and optional uploaded JPG, JPEG, or PNG references up to 5MB each.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the full health log and effects, or 200 OK for a replay.</returns>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> CreateAsync(Guid transportId, [FromBody] HorseHealthLogs.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(transportId, request, AccountId, cancellationToken);
        var message = $"Horse health log for {result.Value.Horse.HorseName} saved successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }

    /// <summary>Record normal health checks for every booked horse.</summary>
    /// <remarks>Only the assigned Escort may record the batch while the transport is READY or IN_TRANSIT. New checks use NORMAL health and hydration, no vital signs, and one shared optional photo uploaded through /api/v1/uploads/signature and referenced by publicId. All checks save atomically without urgent alerts. Replays preserve existing checks.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The log type, optional check time, location, note, and one uploaded JPG, JPEG, or PNG reference up to 5MB.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with createdCount and health log summaries, or 200 OK when no new checks were created.</returns>
    [HttpPost("mark-all-normal")]
    [Consumes("application/json")]
    public async Task<IActionResult> MarkAllNormalAsync(Guid transportId, [FromBody] HorseHealthLogs.Request.RecordRequest request, CancellationToken cancellationToken)
    {
        var result = await service.MarkAllNormalAsync(transportId, request, AccountId, cancellationToken);
        var message = $"Health logs for all {result.Value.Logs.Count} horses saved successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }

    /// <summary>Append photos to an existing horse health check.</summary>
    /// <remarks>Only the assigned Escort who created the check may append images while the transport is READY or IN_TRANSIT. Upload photos through /api/v1/uploads/signature first, then submit their publicIds in files. Existing photos and check fields remain immutable. The total must not exceed five photos. Caption, latitude, longitude, and capturedAt are ignored; photo metadata is returned as null.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="horseHealthLogId">The health log identifier (UUID), belonging to the transport.</param>
    /// <param name="request">One to five uploaded JPG, JPEG, or PNG references up to 5MB each.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with all signed photo URLs, or 422 if the total exceeds five.</returns>
    [HttpPost("{horseHealthLogId}/photos")]
    [Consumes("application/json")]
    public async Task<IActionResult> AddPhotosAsync(Guid transportId, Guid horseHealthLogId,
        [FromBody] HorseHealthLogs.Request.AddPhotosRequest request, CancellationToken cancellationToken)
        => Success(await service.AddPhotosAsync(transportId, horseHealthLogId, request, AccountId, cancellationToken),
            "Horse health log photos added successfully");
}
