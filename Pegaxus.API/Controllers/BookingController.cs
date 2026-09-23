using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("{bookingId:guid}/approve")]
    [Authorize(Policy = JwtExtensions.LogisticsManagerPolicy)]
    public async Task<IActionResult> Approve(Guid bookingId, CancellationToken cancellationToken)
    {
        var response = await _service.ApproveAsync(GetAccountId(), bookingId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Booking approved successfully"));
    }

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
