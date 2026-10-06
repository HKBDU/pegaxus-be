using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Pegaxus.API.Hubs;
using Pegaxus.API.Extensions;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;

using BookingService = Pegaxus.Service.BookingService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/bookings")]
public class BookingController : ControllerBase
{
    private readonly BookingService.IService _service;

    public BookingController(BookingService.IService service)
    {
        _service = service;
    }

    /// <summary>
    /// Record a negotiated booking status.
    /// </summary>
    /// <remarks>A Logistics Manager may change an APPROVED booking to DRAFT or CANCELLED with a required reason. Related dossier operations are suspended and durable notifications are recorded.</remarks>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="request">The negotiated status and required reason.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="hub">The injected hub used to broadcast booking status changes.</param>
    /// <param name="logger">The injected logger for notification broadcast failures.</param>
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
        [FromServices] IHubContext<ComplianceDossierHub> hub, [FromServices] ILogger<BookingController> logger,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.NegotiateAsync(GetAccountId(), bookingId, request, cancellationToken);
        try
        {
            await hub.Clients.All.SendAsync("BookingStatusChanged", response, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The status change and durable notifications are already committed.
            logger.LogWarning(exception, "Unable to broadcast negotiated status for booking {BookingId}", bookingId);
        }
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking status changed; dossier operations suspended.",
            traceId: HttpContext.TraceIdentifier));
    }

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
        var response = await _service.GetListAsync(GetAccountId(), request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Bookings retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
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
    [Authorize(Policy = JwtExtensions.TransportSpecialistOrFleetRouteCoordinatorPolicy)]
    public async Task<IActionResult> GetApprovedList(
        [FromQuery] BookingService.Request.BookingListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetApprovedListAsync(GetAccountId(), request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Bookings retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
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
        var response = await _service.GetAssignmentsAsync(GetAccountId(), bookingId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking assignments retrieved successfully", traceId: HttpContext.TraceIdentifier));
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
        var response = await _service.AssignEmployeeAsync(GetAccountId(), bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking employee assigned successfully", traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Revoke a booking employee assignment.
    /// </summary>
    /// <param name="bookingId">The booking identifier (UUID).</param>
    /// <param name="assignmentId">The assignment identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the revoked assignment.</returns>
    [HttpDelete("{bookingId:guid}/assignments/{assignmentId:guid}")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> RevokeAssignment(Guid bookingId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var response = await _service.RevokeAssignmentAsync(GetAccountId(), bookingId, assignmentId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking employee assignment revoked successfully", traceId: HttpContext.TraceIdentifier));
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
        var response = await _service.GetAsync(GetAccountId(), bookingId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Booking retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
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
        var response = await _service.GetHistoriesAsync(GetAccountId(), bookingId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Booking history retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

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
        await _service.AddHorseIntoBookingAsync(GetAccountId(), bookingId, request, cancellationToken);
        return StatusCode(201, ResponseBuilder.SuccessResponse(
            null,
            "Horse added to booking successfully",
            201,
            HttpContext.TraceIdentifier));
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
            GetAccountId(),
            bookingId,
            horseId,
            request,
            cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Horse handling notes updated successfully",
            traceId: HttpContext.TraceIdentifier));
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
            GetAccountId(),
            bookingId,
            horseId,
            cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Horse removed from booking successfully",
            traceId: HttpContext.TraceIdentifier));
    }

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
            GetAccountId(),
            bookingId,
            request,
            cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Booking status updated successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Create a booking.
    /// </summary>
    /// <remarks>Creates a transport request using horses owned by the authenticated customer.</remarks>
    /// <param name="request">The route, dates, horses, notes, and initial booking status.</param>
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
        var response = await _service.CreateAsync(GetAccountId(), request, cancellationToken);
        return StatusCode(201, ResponseBuilder.SuccessResponse(response, "Booking created successfully", 201));
    }

    /// <summary>
    /// Update a booking.
    /// </summary>
    /// <remarks>Customers may update supplied fields only while the booking is DRAFT.</remarks>
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
        var response = await _service.UpdateAsync(GetAccountId(), bookingId, request, cancellationToken);
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
        await _service.CancelAsync(GetAccountId(), bookingId, request, cancellationToken);
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
        var response = await _service.ApproveAsync(GetAccountId(), bookingId, cancellationToken);
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
        await _service.RejectAsync(GetAccountId(), bookingId, request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Booking rejected successfully"));
    }

    private Guid GetAccountId()
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
            return accountId;
        throw new UnauthorizedException("The authentication session is invalid or has expired");
    }

    private async Task<BadRequestObjectResult?> ValidateRequestAsync<T>(
        T request,
        IValidator<T> validator,
        CancellationToken cancellationToken,
        string errorCode = "VALIDATION_ERROR",
        string message = "The request data is invalid")
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid) return null;
        return BadRequest(ResponseBuilder.ErrorResponse(validation.Errors.Select(error => new
        {
            field = char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..],
            message = error.ErrorMessage
        }), message, errorCode));
    }
}
