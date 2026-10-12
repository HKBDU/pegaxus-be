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
public sealed class InvoicesController(Invoices.IService service) : ControllerBase
{
    /// <summary>Initiate a full online payment for an invoice.</summary>
    /// <remarks>Customers can pay only their own issued DEPOSIT, FINAL or SUPPLEMENTARY invoices in ISSUED or OVERDUE status. Creates a PENDING receipt and replaces older pending receipts. SePay supports whole VND amounts: submit checkoutFields in their returned order as a browser POST to paymentUrl; a GET redirect alone cannot start checkout. Configuration and signing failures roll back receipt changes. Payment confirmation belongs to the webhook.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 with the pending receipt, payment URL and signed checkoutFields; 401 for an invalid session, 403 for a disallowed role, 404 for an unavailable invoice, 409 for an invalid status, 422 for an unpayable type or unsupported currency/amount, or 502 when checkout preparation fails.</returns>
    [HttpPost("{invoiceId:guid}/online-payments")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> InitiateOnlinePayment(Guid invoiceId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        var response = await service.InitiateOnlinePaymentAsync(invoiceId, accountId, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(response,
            "Online payment initiated successfully", StatusCodes.Status201Created, HttpContext.TraceIdentifier));
    }
}
