using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
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
}
