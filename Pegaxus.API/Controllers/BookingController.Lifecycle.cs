using FluentValidation;
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
    /// Change a booking between draft and pending.
    /// </summary>
    /// <remarks>Only DRAFT and PENDING are supported. Submitting a booking requires at least one horse.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The target DRAFT or PENDING status.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the booking status is changed.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnprocessableEntityException">The booking does not contain the horses required for this operation.</exception>
    [HttpPatch("{bookingId:guid}/status")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> ChangeStatus(
        Guid bookingId,
        BookingService.Request.ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        await _service.ChangeStatusAsync(
            AccountId,
            bookingId,
            request,
            cancellationToken);

        return Success(null, "Booking status updated successfully");
    }

    /// <summary>
    /// Create a booking.
    /// </summary>
    /// <remarks>Creates a transport request using horses owned by the authenticated customer. The origin and destination each need an address, a country and a latitude/longitude pair; invalid coordinates return INVALID_COORDINATES.</remarks>
    /// <param name="request">The route with origin and destination coordinates, dates, horses, notes, and initial booking status.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the booking identifiers and status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    [HttpPost]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> Create(
        BookingService.Request.CreateBookingRequest request,
        [FromServices] IValidator<BookingService.Request.CreateBookingRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.CreateAsync(AccountId, request, cancellationToken);
        return StatusCode(201, ResponseBuilder.SuccessResponse(response, "Booking created successfully", 201));
    }

    /// <summary>
    /// Update a booking.
    /// </summary>
    /// <remarks>Customers may update supplied fields only while the booking is DRAFT. Origin and destination coordinates must be sent as latitude/longitude pairs.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The booking fields to update.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated booking identifiers and status.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{bookingId:guid}")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> Update(
        Guid bookingId,
        BookingService.Request.UpdateBookingRequest request,
        [FromServices] IValidator<BookingService.Request.UpdateBookingRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.UpdateAsync(AccountId, bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking updated successfully"));
    }

    /// <summary>
    /// Cancel a booking.
    /// </summary>
    /// <remarks>Customers may cancel a booking only in a status that permits cancellation.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The booking cancellation reason.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the booking is cancelled.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{bookingId:guid}/cancel")]
    [Authorize(Policy = JwtExtensions.CustomerPolicy)]
    public async Task<IActionResult> Cancel(
        Guid bookingId,
        BookingService.Request.CancelBookingRequest request,
        [FromServices] IValidator<BookingService.Request.CancelBookingRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken, "REASON_REQUIRED", "Reason is required");
        if (error is not null) return error;
        await _service.CancelAsync(AccountId, bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Booking cancelled successfully"));
    }

    /// <summary>
    /// Approve a booking.
    /// </summary>
    /// <remarks>A Logistics Manager approves a pending request and creates its transport. The request must contain at least one horse.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the approved booking and transport.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    /// <exception cref="UnprocessableEntityException">The booking does not contain the horses required for this operation.</exception>
    [HttpPost("{bookingId:guid}/approve")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Approve(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.ApproveAsync(AccountId, bookingId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking approved successfully"));
    }

    /// <summary>
    /// Reject a booking.
    /// </summary>
    /// <remarks>A Logistics Manager rejects a pending request and records the rejection reason.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The reason for rejecting the booking.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the booking is rejected.</returns>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPost("{bookingId:guid}/reject")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Reject(
        Guid bookingId,
        BookingService.Request.RejectBookingRequest request,
        [FromServices] IValidator<BookingService.Request.RejectBookingRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken, "REASON_REQUIRED", "Reason is required");
        if (error is not null) return error;
        await _service.RejectAsync(AccountId, bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Booking rejected successfully"));
    }
}
