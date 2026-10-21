using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;

using Incidents = Pegaxus.Service.IncidentService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/emergency-cost-requests")]
[Consumes("application/json")]
public sealed class EmergencyCostRequestsController(Incidents.IIncidentService service) : ApiControllerBase
{
    /// <summary>Propose an emergency cost for an open incident.</summary>
    /// <remarks>CO1: assigned Driver, Escort or RC; transport must be IN_TRANSIT. Amounts are positive whole VND. Optional evidence uses up to five confirmed JPG/PNG uploads. RC accepts the incident as PENDING. Active managers receive HIGH alerts.</remarks>
    /// <param name="incidentReportId">The incident identifier.</param>
    /// <param name="request">Category, amount, description, justification, invoice requirement and uploaded evidence.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the pending request and its version.</returns>
    [HttpPost("/api/v1/incidents/{incidentReportId}/emergency-cost-requests")]
    [Authorize(Policy = JwtExtensions.EmergencyCostProposerPolicy)]
    public async Task<IActionResult> CreateAsync(Guid incidentReportId, [FromBody] Incidents.Request.CreateCostRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateCostAsync(incidentReportId, request, AccountId, cancellationToken);
        return SuccessCreated(result, $"Emergency cost request {result.RequestCode} sent to the logistics manager");
    }

    /// <summary>List emergency costs visible to the employee.</summary>
    /// <remarks>CO2: LM/TS see all requests, RC sees assigned transports, Driver/Escort see their own requests. LM defaults to PENDING. Counts cover the visible scope before filters. Reads never change incident state.</remarks>
    /// <param name="request">Status, category, incident, transport, requested-date range, keyword, sorting and pagination.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with counts, server time and a page of requests.</returns>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.IncidentListViewerPolicy)]
    public async Task<IActionResult> ListAsync([FromQuery] Incidents.Request.CostListRequest request, CancellationToken cancellationToken) =>
        Success(await service.ListCostsAsync(request, AccountId, cancellationToken), "Emergency cost requests retrieved successfully");

    /// <summary>Read an emergency cost and its approval context.</summary>
    /// <remarks>CO3: follows CO2 visibility; out-of-scope requests return 404. Includes incident, visible related requests, current route, latest horse-health counts, signed evidence and role-specific actions.</remarks>
    /// <param name="emergencyCostRequestId">The emergency cost request identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the request and approval context.</returns>
    [HttpGet("{emergencyCostRequestId}")]
    [Authorize(Policy = JwtExtensions.IncidentListViewerPolicy)]
    public async Task<IActionResult> GetAsync(Guid emergencyCostRequestId, CancellationToken cancellationToken) =>
        Success(await service.GetCostAsync(emergencyCostRequestId, AccountId, cancellationToken), "Emergency cost request retrieved successfully");

    /// <summary>Cancel the caller's pending emergency cost.</summary>
    /// <remarks>CO4: only the proposer may cancel. Requires lastUpdatedAt and a reason of at least 10 trimmed characters. Notifies active managers at NORMAL priority.</remarks>
    /// <param name="emergencyCostRequestId">The emergency cost request identifier.</param>
    /// <param name="request">Request version and cancellation reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled request; 409 for stale or non-pending requests.</returns>
    [HttpPost("{emergencyCostRequestId}/cancel")]
    [Authorize(Policy = JwtExtensions.EmergencyCostProposerPolicy)]
    public async Task<IActionResult> CancelAsync(Guid emergencyCostRequestId, [FromBody] Incidents.Request.CancelRequest request, CancellationToken cancellationToken) =>
        Success(await service.CancelCostAsync(emergencyCostRequestId, request, AccountId, cancellationToken), "Emergency cost request cancelled");

    /// <summary>Approve or reject a pending emergency cost.</summary>
    /// <remarks>CO5: LM only, open incident. APPROVE defaults to the requested amount; a reduction requires a reason. REJECT requires a reason and records zero. A decision is immutable. The proposer receives a HIGH alert. Approved amounts feed Flow 6; no actual expenditure is recorded.</remarks>
    /// <param name="emergencyCostRequestId">The emergency cost request identifier.</param>
    /// <param name="request">Request version, APPROVE/REJECT, optional amount and decision reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the decision; 409 with the prior decision for a processed request.</returns>
    [HttpPost("{emergencyCostRequestId}/decision")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> DecideAsync(Guid emergencyCostRequestId, [FromBody] Incidents.Request.CostDecisionRequest request, CancellationToken cancellationToken)
    {
        var result = await service.DecideCostAsync(emergencyCostRequestId, request, AccountId, cancellationToken);
        return Success(result, $"Emergency cost request {result.RequestCode} {result.Status.ToLowerInvariant()}");
    }
}
