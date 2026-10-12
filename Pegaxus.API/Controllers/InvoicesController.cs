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
    /// <summary>Cancel an unpaid invoice with a reason.</summary>
    /// <remarks>Requires an active Logistics Manager and at least 10 characters after trimming the reason. Blocks confirmed or pending payments. Cancellation, CANCEL history and notifications for previously issued invoices commit atomically.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The reason for cancellation.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the cancelled invoice; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status, pending payments or concurrent changes, or 422 for confirmed payment.</returns>
    [HttpPost("{invoiceId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid invoiceId, [FromBody] Invoices.Request.CancelRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return Ok(ResponseBuilder.SuccessResponse(await service.CancelAsync(invoiceId, request, accountId, cancellationToken),
            "Invoice cancelled successfully", traceId: HttpContext.TraceIdentifier));
    }
}
