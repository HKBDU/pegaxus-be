using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using InvoiceService = Pegaxus.Service.InvoiceService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
[Route("api/v1/invoices")]
public sealed class InvoicesController(InvoiceService.IService service) : ControllerBase
{
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
    public async Task<IActionResult> Update(Guid invoiceId, [FromBody] InvoiceService.Request.UpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        var response = await service.UpdateAsync(accountId, invoiceId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Invoice updated successfully", traceId: HttpContext.TraceIdentifier));
    }
}
