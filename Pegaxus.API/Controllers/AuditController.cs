using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using Audit = Pegaxus.Service.AuditService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class AuditController(Audit.IAuditService service) : ApiControllerBase
{
    /// <summary>Search the archive of finished transports.</summary>
    /// <remarks>Only Logistics Managers. Lists COMPLETED transports, and CANCELLED ones when includeCancelled=true. search matches, case-insensitively and anywhere in the text, the horse microchip number, passport number and name, the booking code, the transport code and the customer name. hasClaim=true keeps the transports with at least one claim that is not cancelled. The date range uses the completion time (the cancellation time for a cancelled transport).</remarks>
    /// <param name="request">The search text, filters, sort and page.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a page of transports, their horses and their claim and incident counts.</returns>
    /// <exception cref="BadRequestException">A filter, the sort or the page is not valid.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    [HttpGet("transport-archive")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetArchive([FromQuery] Audit.Request.ArchiveRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetArchiveAsync(AccountId, request, cancellationToken), "Transport archive retrieved successfully");

    /// <summary>Read the audit record of a transport.</summary>
    /// <remarks>A Logistics Manager sees everything (viewMode FULL). A Customer sees a shorter view (viewMode CUSTOMER) of a transport of their own booking: no finance block, no partner price, vehicle or staff, no internal notes, only the pickup and delivery handovers and only HIGH or CRITICAL incidents. blocks picks the blocks to load (booking, horses, routing, compliance, operations, finance); a block the role may not see is left out, not refused. timeline is always returned, oldest first, cut at limit. A transport that is not completed can still be read.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The blocks to load and the timeline limit.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the transport, the requested blocks and the timeline.</returns>
    /// <exception cref="BadRequestException">blocks has an unknown block or limit is out of range.</exception>
    /// <exception cref="ForbiddenException">The caller is neither an active Logistics Manager nor an active Customer.</exception>
    /// <exception cref="NotFoundException">The transport does not exist, or belongs to another customer.</exception>
    [HttpGet("transports/{transportId:guid}/audit")]
    [Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetAudit(Guid transportId, [FromQuery] Audit.Request.AuditRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetAuditAsync(AccountId, transportId, request, cancellationToken), "Transport audit retrieved successfully");

    /// <summary>Ask for an audit package of a completed transport.</summary>
    /// <remarks>Only Logistics Managers. The package is built in the background: this answers 202 at once with a job to poll through GET /audit-exports/{exportId}. Asking again for the same transport, format and mode while the first one is still being built returns that same job. format is ZIP (the original files plus manifest.json with a SHA-256 of each) or PDF (one summary with a seal page). mode is STANDARD or INSURANCE (adds the claims, the incidents in full and the incident photos).</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">The format and the mode.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>202 Accepted with the job.</returns>
    /// <exception cref="BadRequestException">format or mode is not allowed.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The transport does not exist.</exception>
    /// <exception cref="ConflictException">The transport is not completed.</exception>
    [HttpPost("transports/{transportId:guid}/audit-exports")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> RequestExport(Guid transportId, [FromBody] Audit.Request.ExportRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status202Accepted, ResponseBuilder.SuccessResponse(await service.RequestExportAsync(AccountId, transportId, request, cancellationToken),
            "Audit export queued", StatusCodes.Status202Accepted, HttpContext.TraceIdentifier));

    /// <summary>Check an audit export and get its download link.</summary>
    /// <remarks>Only Logistics Managers (the one who asked or another). Poll every 3 to 5 seconds while the status is QUEUED or PROCESSING. A READY package has a signed downloadUrl (delivery DIRECT) valid for 24 hours; a package above 500 MB has delivery EMAIL and no downloadUrl, the link was mailed to the requester. The job is kept for 24 hours after it is ready.</remarks>
    /// <param name="exportId">The export identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the status, and for a READY package the link, size and SHA-256.</returns>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The job does not exist or has expired.</exception>
    [HttpGet("audit-exports/{exportId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetExport(Guid exportId, CancellationToken cancellationToken) =>
        Success(await service.GetExportAsync(AccountId, exportId, cancellationToken), "Audit export retrieved successfully");
}
