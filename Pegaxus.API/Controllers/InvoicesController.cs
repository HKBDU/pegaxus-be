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
    /// <summary>Issue a draft invoice to its booking customer.</summary>
    /// <remarks>Requires an active Logistics Manager and the last observed updatedAt. Checks fee lines, positive total and FINAL deposit deductions. Invoice, ISSUE history, customer notification and email outbox commit atomically.</remarks>
    /// <param name="invoiceId">The invoice identifier (UUID).</param>
    /// <param name="request">The last observed invoice version.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the issued invoice; 400 for invalid input, 401 or 403 for unauthorized access, 404 for a missing invoice, 409 for status, version or deposit conflicts, or 422 for missing fees or a nonpositive total.</returns>
    [HttpPost("{invoiceId:guid}/issue")]
    public async Task<IActionResult> Issue(Guid invoiceId, [FromBody] Invoices.Request.IssueRequest request, CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? User.FindFirstValue("accountId");
        if (!Guid.TryParse(value, out var accountId)) throw new UnauthorizedException("The authenticated account identifier is invalid");
        return Ok(ResponseBuilder.SuccessResponse(await service.IssueAsync(invoiceId, request, accountId, cancellationToken),
            "Invoice issued successfully", traceId: HttpContext.TraceIdentifier));
    }
}
