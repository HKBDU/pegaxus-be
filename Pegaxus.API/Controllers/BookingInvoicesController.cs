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
[Route("api/v1/bookings/{bookingId}/invoices")]
public sealed class BookingInvoicesController(InvoiceService.IService service) : ControllerBase
{
    /// <summary>
    /// List invoices for a booking.
    /// </summary>
    /// <remarks>Active Logistics Managers may view all invoices of any booking. Customers may view only their own booking's previously issued invoices; drafts and invoices cancelled before issuance are excluded before counting and paging. A missing or another customer's booking returns 404 BOOKING_NOT_FOUND. Filter by type and status. Defaults: pageIndex 1, pageSize 20 (maximum 100), createdAt:desc. Sort by createdAt, issuedAt, dueDate or totalAmount with :asc/:desc; null dates sort last. Each item returns the newest non-cancelled transport plan, or null. Empty results return 200 with an empty items array. This endpoint does not modify data.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The optional type, status, sorting and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with invoice list items and pagination totals.</returns>
    /// <exception cref="BadRequestException">The booking identifier or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not an active Customer or Logistics Manager.</exception>
    /// <exception cref="NotFoundException">The booking does not exist or belongs to another customer.</exception>
    [HttpGet]
    public async Task<IActionResult> GetList(Guid bookingId, [FromQuery] InvoiceService.Request.BookingListRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            throw new UnauthorizedException("The authenticated account identifier is invalid.");
        var response = await service.GetByBookingAsync(accountId, bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Invoices retrieved successfully", traceId: HttpContext.TraceIdentifier));
    }
}
