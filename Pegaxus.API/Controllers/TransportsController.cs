using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Transports = Pegaxus.Service.TransportService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.TransportProgressViewerPolicy)]
[Route("api/v1/transports")]
public sealed class TransportsController(Transports.ITransportService service) : ApiControllerBase
{
    /// <summary>List ongoing transports.</summary>
    /// <remarks>OT1: Logistics Managers, Fleet Route Coordinators and Transport Specialists can view all transports. Defaults: READY and IN_TRANSIT, pageIndex 0, pageSize 20, departureAt:asc. Supports search, status and active-incident filters. Returns the effective current segment and serverTime.</remarks>
    /// <param name="request">The status, search, incident, sorting and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with transport items and pagination totals.</returns>
    [HttpGet("ongoing")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetOngoing([FromQuery] Transports.Request.ListRequest request, CancellationToken cancellationToken)
    {
        var response = await service.GetOngoingAsync(AccountId, request, cancellationToken);
        return Success(response, "Ongoing transports retrieved successfully");
    }

    /// <summary>List transports assigned to the current Driver or Escort.</summary>
    /// <remarks>OT2: Assignment comes from non-superseded or already-started segments of confirmed, active or completed route plans. Defaults to READY and IN_TRANSIT. Returns an unpaginated list and serverTime.</remarks>
    /// <param name="request">The optional CSV transport status filter.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the caller's assigned transports.</returns>
    [HttpGet("assigned-to-me")]
    [Authorize(Policy = JwtExtensions.DriverOrEscortPolicy)]
    public async Task<IActionResult> GetAssigned([FromQuery] Transports.Request.AssignedRequest request, CancellationToken cancellationToken)
    {
        var response = await service.GetAssignedAsync(AccountId, request, cancellationToken);
        return Success(response, "Assigned transports retrieved successfully");
    }

    /// <summary>Get a transport journey overview.</summary>
    /// <remarks>OT3: Back-office employees view all transports; Drivers and Escorts must be assigned; Customers see only their bookings with internal fields hidden. Only READY, IN_TRANSIT and COMPLETED transports with a confirmed, active or completed route plan are visible. Includes health, dossiers, incidents and serverTime.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the transport overview.</returns>
    [HttpGet("{transportId}/overview")]
    public async Task<IActionResult> GetOverview(Guid transportId, CancellationToken cancellationToken)
    {
        var response = await service.GetOverviewAsync(AccountId, transportId, cancellationToken);
        return Success(response, "Transport overview retrieved successfully");
    }

    /// <summary>Get effective journey segments and handover boundaries.</summary>
    /// <remarks>OT4: Returns primary history followed by activated backup or return segments, numbered by displayOrder. Unstarted superseded segments are excluded. Customer responses hide internal fields and internal handovers. Provider handover creation remains disabled until B-03. Includes serverTime.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with effective segments and handover boundaries.</returns>
    [HttpGet("{transportId}/segments")]
    public async Task<IActionResult> GetSegments(Guid transportId, CancellationToken cancellationToken)
    {
        var response = await service.GetSegmentsAsync(AccountId, transportId, cancellationToken);
        return Success(response, "Transport segments retrieved successfully");
    }
}
