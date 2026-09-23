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
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.RegisterAsync(request, cancellationToken);
        return StatusCode(201, ResponseBuilder.SuccessResponse(
            response,
            "Registration successful. Check your email for the OTP",
            201));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        AuthService.Request.ForgotPasswordRequest request,
        [FromServices] IValidator<AuthService.Request.ForgotPasswordRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        await _service.ForgotPasswordAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "If the email exists, an OTP has been sent"));
    }

    private async Task<BadRequestObjectResult?> ValidateRequestAsync<T>(
        T request,
        IValidator<T> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid) return null;
        var errors = validation.Errors.Select(validationError => new
        {
            field = char.ToLowerInvariant(validationError.PropertyName[0]) + validationError.PropertyName[1..],
            message = validationError.ErrorMessage
        });
        var errorCode = validation.Errors.Any(error =>
            error.PropertyName == "ConfirmPassword" && error.ErrorMessage.Contains("match", StringComparison.OrdinalIgnoreCase))
            ? "PASSWORD_MISMATCH"
            : "VALIDATION_ERROR";
        return BadRequest(ResponseBuilder.ErrorResponse(
            errors,
            errorCode == "PASSWORD_MISMATCH" ? "Password confirmation does not match" : "The request data is invalid",
            errorCode));
    }
}
