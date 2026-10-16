using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using HorseHealth = Pegaxus.Service.HorseHealthService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.TransportProgressViewerPolicy)]
[Route("api/v1/horse-health-logs")]
public sealed class HorseHealthLogsController(HorseHealth.IHorseHealthService service) : ApiControllerBase
{
    /// <summary>Get horse health for a transport.</summary>
    /// <remarks>Back-office employees, assigned Drivers and Escorts, and the booking Customer may view every booked horse and its latest check. Customer responses hide the current Escort phone.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the current Escort and latest health check for each booked horse.</returns>
    [HttpGet("/api/v1/transports/{transportId}/horse-health")]
    public async Task<IActionResult> GetOverviewAsync(Guid transportId, CancellationToken cancellationToken)
        => Success(await service.GetOverviewAsync(transportId, AccountId, cancellationToken), "Horse health overview retrieved successfully");

    /// <summary>Get a horse health check history.</summary>
    /// <remarks>Back-office employees, assigned Drivers and Escorts, and the booking Customer may view checks newest first. Optional logType and healthStatus filters accept comma-separated DB enum names.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="horseId">The booked horse identifier (UUID).</param>
    /// <param name="request">Optional log type, health status, and pagination. Defaults: pageIndex 0, pageSize 20; maximum pageSize 100.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the horse and a page of health checks.</returns>
    [HttpGet("/api/v1/transports/{transportId}/horses/{horseId}/health-logs")]
    public async Task<IActionResult> GetLogsAsync(Guid transportId, Guid horseId, [FromQuery] HorseHealth.Request.ListRequest request,
        CancellationToken cancellationToken)
        => Success(await service.GetLogsAsync(transportId, horseId, request, AccountId, cancellationToken), "Horse health logs retrieved successfully");

    /// <summary>Get a horse health check.</summary>
    /// <remarks>Back-office employees, assigned Drivers and Escorts, and the booking Customer may view an immutable check. Customer responses hide veterinarianNote. Photo URLs are signed after authorization.</remarks>
    /// <param name="horseHealthLogId">The horse health log identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the health check, historical recorder, vital signs, notes, and photos.</returns>
    [HttpGet("{horseHealthLogId}")]
    public async Task<IActionResult> GetAsync(Guid horseHealthLogId, CancellationToken cancellationToken)
        => Success(await service.GetAsync(horseHealthLogId, AccountId, cancellationToken), "Horse health log retrieved successfully");
}
