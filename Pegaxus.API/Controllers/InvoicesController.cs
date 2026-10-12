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
[Authorize(Policy = JwtExtensions.InvoiceViewerPolicy)]
public sealed class InvoicesController(Invoices.IService service) : ControllerBase
{
    /// <summary>Get an invoice's status history.</summary>
    /// <remarks>Logistics Managers see all events. Customers see only their own previously issued invoices, excluding CREATE and new-status DRAFT events. Returns a chronological, read-only timeline with employee or System attribution.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a history array, including an empty array if no events exist; 401 for an invalid session, 403 for a disallowed role or an unissued customer invoice, or 404 for a missing or another customer's invoice.</returns>
    [HttpGet("{invoiceId:guid}/status-history")]
    public async Task<IActionResult> GetStatusHistory(Guid invoiceId, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return Ok(ResponseBuilder.SuccessResponse(await service.GetStatusHistoryAsync(invoiceId, accountId, cancellationToken),
            "Invoice status history retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }
}
