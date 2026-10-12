using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using InvoiceService = Pegaxus.Service.InvoiceService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.CustomerOrLogisticsManagerPolicy)]
[Route("api/v1/invoices")]
public sealed class InvoicesController(InvoiceService.IService service) : ControllerBase
{
    /// <summary>Cancel an unpaid invoice with a reason.</summary>
    /// <remarks>Requires an active Logistics Manager and at least 10 characters after trimming the reason. Blocks confirmed or pending payments. Cancellation, CANCEL history and notifications for previously issued invoices commit atomically.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The reason for cancellation.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled invoice; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status, pending payments or concurrent changes, or 422 for confirmed payment.</returns>
    [HttpPost("{invoiceId:guid}/cancel")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Cancel(Guid invoiceId, [FromBody] InvoiceService.Request.CancelRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return Ok(ResponseBuilder.SuccessResponse(await service.CancelAsync(invoiceId, request, accountId, cancellationToken),
            "Invoice cancelled successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Record full manual payment or a credit note payout.</summary>
    /// <remarks>Requires an active Logistics Manager. Confirms one receipt, fails pending receipts, marks the invoice PAID and records history and customer notifications atomically. A deposit approves the latest eligible transport plan; a paid FINAL unlocks credit notes.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The full amount, matching currency, payment method and optional transaction details.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with receipt, invoice and effects; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status or concurrent changes, or 422 for an unpayable type, partial payment or currency mismatch.</returns>
    [HttpPost("{invoiceId:guid}/receipts")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> RecordPayment(Guid invoiceId, [FromBody] InvoiceService.Request.RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return StatusCode(201, ResponseBuilder.SuccessResponse(await service.RecordPaymentAsync(invoiceId, request, accountId, cancellationToken),
            "Payment recorded successfully", 201, HttpContext.TraceIdentifier));
    }

    /// <summary>Issue a draft invoice to its booking customer.</summary>
    /// <remarks>Requires an active Logistics Manager and the last observed updatedAt. Checks fee lines, positive total and FINAL deposit deductions. Invoice, ISSUE history, customer notification and email outbox commit atomically.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The last observed invoice version.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the issued invoice; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status, version or deposit conflicts, or 422 for missing fees or a nonpositive total.</returns>
    [HttpPost("{invoiceId:guid}/issue")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Issue(Guid invoiceId, [FromBody] InvoiceService.Request.IssueRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return Ok(ResponseBuilder.SuccessResponse(await service.IssueAsync(invoiceId, request, accountId, cancellationToken),
            "Invoice issued successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>Get invoice details.</summary>
    /// <remarks>Active Logistics Managers may view every invoice. Customers may view only previously issued invoices of their own bookings. A missing invoice or another customer's invoice returns 404 INVOICE_NOT_FOUND; an owned draft or invoice cancelled before issuance returns 403 FORBIDDEN. Includes ordered cost lines, deposit deductions, customer and contact details, booking, transport, newest non-cancelled transport plan, receipts and actions allowed for the caller. Payment and item dates remain null until the pending Flow 6 fields exist. This endpoint does not modify data.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the full invoice details.</returns>
    /// <exception cref="BadRequestException">The invoice identifier is invalid.</exception>
    /// <exception cref="ForbiddenException">The caller's role is not allowed or the owned invoice has not been issued.</exception>
    /// <exception cref="NotFoundException">The invoice does not exist or belongs to another customer.</exception>
    [HttpGet("{invoiceId}")]
    public async Task<IActionResult> Get(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        return Ok(ResponseBuilder.SuccessResponse(await service.GetAsync(accountId, invoiceId, cancellationToken),
            "Invoice retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }


    /// <summary>
    /// Get all invoices.
    /// </summary>
    /// <remarks>Only active Logistics Managers may view the system invoice list, including drafts and cancelled invoices. Filter by type, status, booking, transport plan, inclusive issue/due date ranges, or search invoice number, booking code and customer name. A transport plan filter selects invoices for its transport; each item returns the newest non-cancelled plan. Defaults: pageIndex 1, pageSize 20 (maximum 100), createdAt:desc. Sort by createdAt, issuedAt, dueDate or totalAmount with :asc/:desc; null dates sort last. Empty results return 200 with an empty items array. This endpoint does not modify data.</remarks>
    /// <param name="request">The optional filters, search, sort and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with invoice list items and pagination totals.</returns>
    /// <exception cref="BadRequestException">Pagination, filter values, date ranges or sort parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is a Customer or is not an active Logistics Manager.</exception>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> GetList([FromQuery] InvoiceService.Request.ListRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        var response = await service.GetListAsync(accountId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Invoices retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Update a draft invoice.
    /// </summary>
    /// <remarks>Only active Logistics Managers may edit DRAFT invoices. Supply the latest lastUpdatedAt; type is immutable. Items replace the complete list: existing identifiers keep their rows, missing rows are deleted and rows without identifiers are added. The server recalculates fixed 20 percent tax and regenerates FINAL paid-deposit deductions atomically. Empty drafts are allowed. Payment and itemDate are ignored pending database support. No status history or notifications are created.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The latest invoice timestamp, EUR or VND currency, due date, note and replacement items.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the complete updated draft invoice and its new updatedAt.</returns>
    /// <exception cref="BadRequestException">Request fields, immutable type, item identifiers or horse references are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The invoice does not exist.</exception>
    /// <exception cref="ConflictException">The invoice is locked, the timestamp is stale or data changed concurrently.</exception>
    /// <exception cref="UnprocessableEntityException">FINAL currency differs from a paid deposit's currency.</exception>
    [HttpPut("{invoiceId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Update(Guid invoiceId, [FromBody] InvoiceService.Request.UpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        var response = await service.UpdateAsync(accountId, invoiceId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Invoice updated successfully", traceId: HttpContext.TraceIdentifier));
    }
}
