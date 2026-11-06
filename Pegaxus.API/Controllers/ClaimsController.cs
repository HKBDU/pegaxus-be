using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using ClaimService = Pegaxus.Service.ClaimService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class ClaimsController(ClaimService.IClaimService service) : ApiControllerBase
{
    /// <summary>Check whether a booking can receive a claim, and until when.</summary>
    /// <remarks>A customer sees only their own booking; a Logistics Manager sees any. Always 200 for the booking owner: blockedReason says why a claim cannot be submitted (HANDOVER_NOT_COMPLETED, CLAIM_WINDOW_EXPIRED). The window is 24 hours after the delivery handover was signed; count the time down with remainingSeconds and serverTime. Writes nothing.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the delivery handover, deadline, remaining seconds, canSubmit, blockedReason and the number of live claims.</returns>
    /// <exception cref="ForbiddenException">The caller is neither an active Customer nor an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The booking does not exist or belongs to another customer.</exception>
    [HttpGet("bookings/{bookingId:guid}/claim-eligibility")]
    [Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetEligibility(Guid bookingId, CancellationToken cancellationToken) =>
        Success(await service.GetEligibilityAsync(AccountId, bookingId, cancellationToken), "Claim eligibility retrieved successfully");

    /// <summary>Submit a claim about a delivery.</summary>
    /// <remarks>Only the booking Customer. The claim is filed against the latest signed DELIVERY handover of the booking and only within 24 hours of its signature. A customer may file several claims. The server assigns the code (CLM-YYYYMMDD-XXXX), the customer and the VND currency. All Logistics Managers are notified and the customer gets a confirmation email.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The claim type, title, description and optional requested compensation, evidence note and customer note.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the customer view of the claim.</returns>
    /// <exception cref="BadRequestException">A field is invalid, for example a fractional amount.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Customer.</exception>
    /// <exception cref="NotFoundException">The booking does not exist or belongs to another customer.</exception>
    /// <exception cref="UnprocessableEntityException">The delivery handover is not signed yet, or the 24 hour claim period has expired.</exception>
    [HttpPost("bookings/{bookingId:guid}/claims")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> Submit(Guid bookingId, [FromBody] ClaimService.Request.SubmitRequest request, CancellationToken cancellationToken) =>
        SuccessCreated(await service.SubmitAsync(AccountId, bookingId, request, cancellationToken), "Claim submitted successfully");

    /// <summary>List claims.</summary>
    /// <remarks>A Logistics Manager sees every claim; a Customer only their own. Filter by status and type (comma-separated), booking, submission date range (UTC days) or search the claim code, booking code and customer name.</remarks>
    /// <param name="request">The optional filters, search, sort and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with claim list items and pagination totals.</returns>
    /// <exception cref="BadRequestException">Pagination, filters, date range or sort are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is neither an active Customer nor an active Logistics Manager.</exception>
    [HttpGet("claims")]
    [Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
    public async Task<IActionResult> GetList([FromQuery] ClaimService.Request.ListRequest request, CancellationToken cancellationToken) =>
        Success(await service.GetListAsync(AccountId, request, cancellationToken), "Claims retrieved successfully");

    /// <summary>Get one claim.</summary>
    /// <remarks>A Customer gets the basic view of their own claim: no internal note, corrective action, pickup handover, health summary, incidents or billing. A Logistics Manager gets everything. The first time a manager opens a pending claim, reviewedAt is set and updatedAt moves with it, so use the returned updatedAt for the next change.</remarks>
    /// <param name="claimId">The claim identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the claim.</returns>
    /// <exception cref="ForbiddenException">The caller is neither an active Customer nor an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The claim does not exist or belongs to another customer.</exception>
    [HttpGet("claims/{claimId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
    public async Task<IActionResult> Get(Guid claimId, CancellationToken cancellationToken) =>
        Success(await service.GetAsync(AccountId, claimId, cancellationToken), "Claim retrieved successfully");

    /// <summary>Record the outcome of a claim, including a rejection.</summary>
    /// <remarks>Only a Logistics Manager, only for a PENDING claim without an outcome. A rejection is resolutionType NO_COMPENSATION. COMPENSATION, PARTIAL_COMPENSATION and REFUND need an approved amount above 0; NO_COMPENSATION and SERVICE_RECOVERY need 0 or none. APPROVED means the company agrees to compensate but has not done so yet; confirm that later with the complete action. The customer is notified without the internal note and corrective action.</remarks>
    /// <param name="claimId">The claim identifier (UUID).</param>
    /// <param name="request">The latest claim timestamp, resolution type, approved amount, summary and internal fields.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the manager view of the claim, status APPROVED or REJECTED.</returns>
    /// <exception cref="BadRequestException">A field is invalid or the amount does not fit the resolution type.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The claim does not exist.</exception>
    /// <exception cref="ConflictException">The claim already has an outcome, is not PENDING, or the timestamp is stale.</exception>
    [HttpPost("claims/{claimId:guid}/resolution")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Resolve(Guid claimId, [FromBody] ClaimService.Request.ResolveRequest request, CancellationToken cancellationToken)
    {
        var claim = await service.ResolveAsync(AccountId, claimId, request, cancellationToken);
        return SuccessCreated(claim, claim.Status == "REJECTED" ? "Claim rejected successfully" : "Claim approved successfully");
    }

    /// <summary>Cancel a claim.</summary>
    /// <remarks>The customer who filed it, or a Logistics Manager, while it is still PENDING. The reason must be 10 to 2000 characters. The other side is notified.</remarks>
    /// <param name="claimId">The claim identifier (UUID).</param>
    /// <param name="request">The latest claim timestamp and the cancellation reason.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled claim in the view of the caller.</returns>
    /// <exception cref="BadRequestException">The reason is too short or too long.</exception>
    /// <exception cref="ForbiddenException">The caller is neither an active Customer nor an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The claim does not exist or belongs to another customer.</exception>
    /// <exception cref="ConflictException">The claim is not PENDING or the timestamp is stale.</exception>
    [HttpPost("claims/{claimId:guid}/cancel")]
    [Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
    public async Task<IActionResult> Cancel(Guid claimId, [FromBody] ClaimService.Request.CancelRequest request, CancellationToken cancellationToken) =>
        Success(await service.CancelAsync(AccountId, claimId, request, cancellationToken), "Claim cancelled successfully");

    /// <summary>Confirm that an approved claim has been compensated.</summary>
    /// <remarks>Only a Logistics Manager, only for an APPROVED claim. Closes the claim as RESOLVED and notifies the customer by web and email. It is not triggered by paying a credit note, because claims and invoices are not linked in the database.</remarks>
    /// <param name="claimId">The claim identifier (UUID).</param>
    /// <param name="request">The latest claim timestamp.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the manager view of the resolved claim.</returns>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The claim does not exist.</exception>
    /// <exception cref="ConflictException">The claim is not APPROVED or the timestamp is stale.</exception>
    [HttpPost("claims/{claimId:guid}/complete")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Complete(Guid claimId, [FromBody] ClaimService.Request.CompleteRequest request, CancellationToken cancellationToken) =>
        Success(await service.CompleteAsync(AccountId, claimId, request, cancellationToken), "Claim completed successfully");
}
