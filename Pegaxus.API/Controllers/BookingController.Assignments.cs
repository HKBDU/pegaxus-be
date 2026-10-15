using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;

using BookingService = Pegaxus.Service.BookingService;

namespace Pegaxus.API.Controllers;

public sealed partial class BookingController
{
    /// <summary>
    /// Record a negotiated booking status.
    /// </summary>
    /// <remarks>A Logistics Manager may change an APPROVED booking to DRAFT or CANCELLED with a required reason. Related dossier operations are suspended and durable notifications are recorded.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The negotiated status and required reason.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated booking and dossier statuses.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="ConflictException">The operation conflicts with the current state, a duplicate record, or a concurrent change.</exception>
    [HttpPatch("{bookingId:guid}/negotiated-status")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Negotiate(Guid bookingId, [FromBody] BookingService.Request.NegotiateBookingRequest request,
        [FromServices] IValidator<BookingService.Request.NegotiateBookingRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.NegotiateAsync(AccountId, bookingId, request, cancellationToken);
        return Success(response, "Booking status changed; dossier operations suspended.");
    }

    /// <summary>
    /// List booking employee assignments.
    /// </summary>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with active and historical assignments.</returns>
    [HttpGet("{bookingId:guid}/assignments")]
    [Authorize(Policy = JwtExtensions.BookingAssignmentViewerPolicy)]
    public async Task<IActionResult> GetAssignments(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.GetAssignmentsAsync(AccountId, bookingId, cancellationToken);
        return Success(response, "Booking assignments retrieved successfully");
    }

    /// <summary>
    /// Assign a Transport Specialist or Fleet Route Coordinator to a booking.
    /// </summary>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The target employee and assignment role.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the active assignment.</returns>
    [HttpPut("{bookingId:guid}/assignments")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> AssignEmployee(Guid bookingId, [FromBody] BookingService.Request.AssignBookingEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.AssignEmployeeAsync(AccountId, bookingId, request, cancellationToken);
        return Success(response, "Booking employee assigned successfully");
    }

    /// <summary>
    /// Revoke a booking employee assignment.
    /// </summary>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="employeeId">The assigned employee identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the revoked assignment.</returns>
    [HttpDelete("{bookingId:guid}/assignments/{employeeId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> RevokeAssignment(Guid bookingId, Guid employeeId, CancellationToken cancellationToken)
    {
        var response = await _service.RevokeAssignmentAsync(AccountId, bookingId, employeeId, cancellationToken);
        return Success(response, "Booking employee assignment revoked successfully");
    }
}
