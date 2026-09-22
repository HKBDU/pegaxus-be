using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Models;

using AuthService = Pegaxus.Service.Auth;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService.IService _service;

    public AuthController(AuthService.IService service)
    {
        _service = service;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        AuthService.Request.RegisterRequest request,
        [FromServices] IValidator<AuthService.Request.RegisterRequest> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors.Select(error => new
            {
                field = char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..],
                message = error.ErrorMessage
            });

            return BadRequest(ResponseBuilder.ErrorResponse(
                errors,
                "The request data is invalid",
                HttpContext.TraceIdentifier));
        }

        var response = await _service.RegisterAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Registration successful. Check your email for the OTP.",
            HttpContext.TraceIdentifier));
    }
}
