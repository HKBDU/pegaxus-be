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
    /// Get all invoices.
    /// </summary>
    /// <remarks>Only active Logistics Managers may view the system invoice list, including drafts and cancelled invoices. Filter by type, status, booking, transport plan, inclusive issue/due date ranges, or search invoice number, booking code and customer name. A transport plan filter selects invoices for its transport; each item returns the newest non-cancelled plan. Defaults: pageIndex 1, pageSize 20 (maximum 100), createdAt:desc. Sort by createdAt, issuedAt, dueDate or totalAmount with :asc/:desc; null dates sort last. Empty results return 200 with an empty items array. This endpoint does not modify data.</remarks>
    /// <param name="request">The optional filters, search, sort and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with invoice list items and pagination totals.</returns>
    /// <exception cref="BadRequestException">Pagination, filter values, date ranges or sort parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is a Customer or is not an active Logistics Manager.</exception>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] InvoiceService.Request.ListRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        var response = await service.GetListAsync(accountId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Invoices retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }
}
