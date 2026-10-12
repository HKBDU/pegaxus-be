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
}
