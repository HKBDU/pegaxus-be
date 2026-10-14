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
    /// Add a horse to a booking.
    /// </summary>
    /// <remarks>The customer may add an owned horse only while the booking is editable.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The owned horse identifier and optional handling notes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created after the horse is added.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{bookingId:guid}/horses")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> AddHorse(
        Guid bookingId,
        BookingService.Request.AddHorseRequest request,
        CancellationToken cancellationToken)
    {
        await _service.AddHorseIntoBookingAsync(AccountId, bookingId, request, cancellationToken);
        return SuccessCreated(null, "Horse added to booking successfully");
    }

    /// <summary>
    /// Update horse handling notes in a booking.
    /// </summary>
    /// <remarks>Changes the booking-specific handling notes without modifying the horse profile.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="horseId">The horse identifier (UUID).</param>
    /// <param name="request">The replacement special handling notes.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the handling notes are updated.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{bookingId:guid}/horses/{horseId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> UpdateHorseNotes(
        Guid bookingId,
        Guid horseId,
        BookingService.Request.UpdateHorseNotesRequest request,
        CancellationToken cancellationToken)
    {
        await _service.UpdateHorseNotesAsync(
            AccountId,
            bookingId,
            horseId,
            request,
            cancellationToken);

        return Success(null, "Horse handling notes updated successfully");
    }

    /// <summary>
    /// Remove a horse from a booking.
    /// </summary>
    /// <remarks>Removes the horse from an editable booking without deleting its profile.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="horseId">The horse identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the horse is removed.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpDelete("{bookingId:guid}/horses/{horseId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> RemoveHorse(
        Guid bookingId,
        Guid horseId,
        CancellationToken cancellationToken)
    {
        await _service.RemoveHorseAsync(
            AccountId,
            bookingId,
            horseId,
            cancellationToken);

        return Success(null, "Horse removed from booking successfully");
    }
}
