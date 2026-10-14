using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using BookingService = Pegaxus.Service.BookingService;

namespace Pegaxus.API.Controllers;

public sealed partial class BookingController
{
    /// <summary>
    /// List bookings.
    /// </summary>
    /// <remarks>Customers see their own bookings; Logistics Managers see non-draft bookings across customers.</remarks>
    /// <param name="request">The search, status, sorting, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated booking list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    [HttpGet]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetList(
        [FromQuery] BookingService.Request.BookingListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetListAsync(AccountId, request, cancellationToken);
        return Success(response, "Bookings retrieved successfully");
    }

    /// <summary>
    /// List approved bookings for transport planning and compliance work.
    /// </summary>
    /// <remarks>Transport Specialists and Fleet Route Coordinators can search, filter, sort, and paginate bookings. The response includes the latest transport plan route and compliance dossier summaries when available.</remarks>
    /// <param name="request">The status, search, sorting, and pagination filters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated list of bookings and related planning summaries.</returns>
    /// <exception cref="ForbiddenException">The caller is not a Transport Specialist or Fleet Route Coordinator.</exception>
    /// <exception cref="BadRequestException">The query fields or status filter are invalid.</exception>
    [HttpGet("approved")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetApprovedList(
        [FromQuery] BookingService.Request.ApprovedBookingListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetApprovedListAsync(AccountId, request, cancellationToken);
        return Success(response, "Approved bookings retrieved successfully");
    }

    /// <summary>
    /// Get an approved booking detail for transport planning work.
    /// </summary>
    /// <param name="bookingId">The approved booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with booking, transport plan, compliance dossier, and route plans.</returns>
    [HttpGet("approved/{bookingId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetApproved(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.GetApprovedAsync(AccountId, bookingId, cancellationToken);
        return Success(response, "Approved booking retrieved successfully");
    }

    /// <summary>
    /// Get booking details.
    /// </summary>
    /// <remarks>Customers may only view their own bookings. Logistics Managers may view non-draft bookings across customers.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with booking, horse, customer, and transport details.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{bookingId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(AccountId, bookingId, cancellationToken);
        return Success(response, "Booking retrieved successfully");
    }

    /// <summary>
    /// Get booking status history.
    /// </summary>
    /// <remarks>Returns status changes from oldest to newest for a booking visible to the caller.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the booking status history.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpGet("{bookingId:guid}/histories")]
    [Authorize(Policy = JwtExtensions.CustomerOrEmployeePolicy)]
    public async Task<IActionResult> GetHistories(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.GetHistoriesAsync(AccountId, bookingId, cancellationToken);
        return Success(response, "Booking history retrieved successfully");
    }
}
