using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using Invoices = Pegaxus.Service.InvoiceService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/invoices")]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
public sealed class InvoicesController(Invoices.IService service) : ControllerBase
{
    /// <summary>Record full manual payment or a credit note payout.</summary>
    /// <remarks>Requires an active Logistics Manager. Confirms one receipt, fails pending receipts, marks the invoice PAID and records history and customer notifications atomically. A deposit approves the latest eligible transport plan; a paid FINAL unlocks credit notes.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The full amount, matching currency, payment method and optional transaction details.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with receipt, invoice and effects; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status or concurrent changes, or 422 for an unpayable type, partial payment or currency mismatch.</returns>
    [HttpPost("{invoiceId:guid}/receipts")]
    public async Task<IActionResult> RecordPayment(Guid invoiceId, [FromBody] Invoices.Request.RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return StatusCode(201, ResponseBuilder.SuccessResponse(await service.RecordPaymentAsync(invoiceId, request, accountId, cancellationToken),
            "Payment recorded successfully", 201, HttpContext.TraceIdentifier));
    }
}
