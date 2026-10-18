using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Handovers = Pegaxus.Service.HandoverService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/handover-records")]
public sealed class HandoverRecordsController(Handovers.IHandoverService service) : ApiControllerBase
{
    /// <summary>Create a handover record.</summary>
    /// <remarks>LM/RC or Driver/Escort assigned to a linked segment. Only LM/RC may schedule with startNow=false. Parties and boundary snapshots are inferred; OTHER requires explicit parties. Creation does not change journey state. Duplicate active boundaries return 409 HANDOVER_ALREADY_EXISTS.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">Handover type, boundary segments, scheduling and optional snapshot overrides.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the complete handover record.</returns>
    [Authorize(Policy = JwtExtensions.HandoverOperatorPolicy)]
    [HttpPost("~/api/v1/transports/{transportId:guid}/handover-records")]
    [Consumes("application/json")]
    public async Task<IActionResult> CreateAsync(Guid transportId, [FromBody] Handovers.Request.CreateRequest request, CancellationToken cancellationToken) =>
        SuccessCreated(await service.CreateAsync(transportId, request, AccountId, cancellationToken), "Handover record created successfully");

    /// <summary>List a transport's handover records.</summary>
    /// <remarks>LM, RC and TS may view all eligible transports; Driver and Escort must be assigned. Customers see only PICKUP and DELIVERY records belonging to their booking. Pagination starts at zero. Filters accept comma-separated enum names.</remarks>
    /// <param name="transportId">The transport identifier (UUID).</param>
    /// <param name="request">Optional handoverType, status, pageIndex (0), pageSize (20), and sortBy (sequenceNo:asc or sequenceNo:desc).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with serverTime, items and pagination totals.</returns>
    [Authorize(Policy = JwtExtensions.HandoverViewerPolicy)]
    [HttpGet("~/api/v1/transports/{transportId:guid}/handover-records")]
    public async Task<IActionResult> GetListAsync(Guid transportId, [FromQuery] Handovers.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(transportId, request, AccountId, cancellationToken), "Handover records retrieved successfully");

    /// <summary>Get a handover record.</summary>
    /// <remarks>Customers may view only their own PICKUP and DELIVERY records; employee contact phones are hidden. Other customer requests return 404 HANDOVER_RECORD_NOT_FOUND. Available actions reflect the caller's current assignment and the record status.</remarks>
    /// <param name="handoverRecordId">The handover record identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the parties, segments, signatures, notes, timestamps and available actions.</returns>
    [Authorize(Policy = JwtExtensions.HandoverViewerPolicy)]
    [HttpGet("{handoverRecordId:guid}")]
    public async Task<IActionResult> GetAsync(Guid handoverRecordId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(handoverRecordId, AccountId, cancellationToken), "Handover record retrieved successfully");

    /// <summary>Start a planned handover record.</summary>
    /// <remarks>No request body. Only LM, RC, or Driver/Escort assigned to either linked segment may start the record. Only PLANNED may become IN_PROGRESS; other statuses return 409 INVALID_HANDOVER_STATUS. Does not start the transport or any segment.</remarks>
    /// <param name="handoverRecordId">The handover record identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the IN_PROGRESS handover record and its new updatedAt.</returns>
    [Authorize(Policy = JwtExtensions.HandoverOperatorPolicy)]
    [HttpPost("{handoverRecordId:guid}/start")]
    public async Task<IActionResult> StartAsync(Guid handoverRecordId, CancellationToken cancellationToken) =>
        Success(await service.StartAsync(handoverRecordId, AccountId, cancellationToken), "Handover record started successfully");

    /// <summary>Complete a handover and apply its transport lifecycle effects.</summary>
    /// <remarks>LM/RC or Driver/Escort assigned to either linked segment. Submit JSON with lastUpdatedAt, three required notes and the publicIds of both PNG signatures (up to 200 KB each) uploaded through /api/v1/uploads/signature. PLANNED and IN_PROGRESS may complete. Stale versions return 409 HANDOVER_MODIFIED; terminal records return 409 INVALID_HANDOVER_STATUS; changed segment boundaries or parties return 422 INVALID_SEGMENT_PAIR. Signatures, lifecycle changes, notifications and delivery email commit together. GPS arrival timestamps are preserved.</remarks>
    /// <param name="handoverRecordId">The handover record identifier (UUID).</param>
    /// <param name="request">Notes, optional snapshot overrides and both signature publicIds.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the COMPLETED record, lifecycle effects and the delivery claim deadline.</returns>
    [Authorize(Policy = JwtExtensions.HandoverOperatorPolicy)]
    [HttpPost("{handoverRecordId:guid}/complete")]
    [Consumes("application/json")]
    public async Task<IActionResult> CompleteAsync(Guid handoverRecordId, [FromBody] Handovers.Request.CompleteRequest request, CancellationToken cancellationToken) =>
        Success(await service.CompleteAsync(handoverRecordId, request, AccountId, cancellationToken), "Handover completed successfully");

    /// <summary>Cancel a planned or in-progress handover record.</summary>
    /// <remarks>Only LM/RC. Requires lastUpdatedAt from the latest record and a cancellationReason of at least 10 characters after trimming. Stale versions return 409 HANDOVER_MODIFIED. Completed/cancelled records are immutable. Cancellation and HIGH priority notifications to assigned RCs and active employee parties commit atomically; journey timestamps remain unchanged.</remarks>
    /// <param name="handoverRecordId">The handover record identifier (UUID).</param>
    /// <param name="request">The latest lastUpdatedAt and cancellationReason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the CANCELLED handover record and no available actions.</returns>
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrFleetRouteCoordinatorPolicy)]
    [HttpPost("{handoverRecordId:guid}/cancel")]
    public async Task<IActionResult> CancelAsync(Guid handoverRecordId, [FromBody] Handovers.Request.CancelRequest request, CancellationToken cancellationToken) =>
        Success(await service.CancelAsync(handoverRecordId, request, AccountId, cancellationToken), "Handover record cancelled successfully");
}
