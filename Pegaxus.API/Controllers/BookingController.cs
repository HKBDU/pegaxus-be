using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Hubs;
using Pegaxus.API.Models;

using BookingService = Pegaxus.Service.BookingService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/bookings")]
public partial class BookingController : ApiControllerBase
{
    private readonly BookingService.IBookingService _service;

    public BookingController(BookingService.IBookingService service)
    {
        _service = service;
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
