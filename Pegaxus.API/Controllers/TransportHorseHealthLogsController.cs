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
    /// <summary>Record a horse health check and optional photo.</summary>
    /// <remarks>Only the assigned Escort may record checks while the transport is READY or IN_TRANSIT. Critical checks or veterinarian requests notify managers and assigned coordinators atomically. A repeated transport, horse, Escort, log type, and checkedAt returns the existing immutable check with no repeated effects. Only the first image is stored; GPS and photo metadata are ignored until B-04/B-05.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The horse, health check fields, and optional JPG, JPEG, or PNG files up to 5MB each.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the full health log and effects, or 200 OK for a replay.</returns>
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateAsync(Guid transportId, [FromForm] HorseHealthLogs.Request.CreateRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(transportId, request, AccountId, cancellationToken);
        var message = $"Horse health log for {result.Value.Horse.HorseName} saved successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }

    /// <summary>Record normal health checks for every booked horse.</summary>
    /// <remarks>Only the assigned Escort may record the batch while the transport is READY or IN_TRANSIT. New checks use NORMAL health and hydration, no vital signs, and one shared optional photo. All checks save atomically without urgent alerts. Replays preserve existing checks.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The log type, optional check time, location, note, and first JPG, JPEG, or PNG image up to 5MB.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with createdCount and health log summaries, or 200 OK when no new checks were created.</returns>
    [HttpPost("mark-all-normal")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> MarkAllNormalAsync(Guid transportId, [FromForm] HorseHealthLogs.Request.RecordRequest request, CancellationToken cancellationToken)
    {
        var result = await service.MarkAllNormalAsync(transportId, request, AccountId, cancellationToken);
        var message = $"Health logs for all {result.Value.Logs.Count} horses saved successfully";
        return result.Created ? SuccessCreated(result.Value, message) : Success(result.Value, message);
    }
}
