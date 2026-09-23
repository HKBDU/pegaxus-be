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

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        AuthService.Request.LoginRequest request,
        [FromServices] IValidator<AuthService.Request.LoginRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequestAsync(request, validator, cancellationToken);
        if (validationError is not null)
            return validationError;

        var response = await _service.LoginAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Login successful. Welcome back!", traceId: HttpContext.TraceIdentifier));
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

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(
        AuthService.Request.VerifyEmailRequest request,
        [FromServices] IValidator<AuthService.Request.VerifyEmailRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        await _service.VerifyEmailAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Email verified successfully"));
    }

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(
        AuthService.Request.ResendOtpRequest request,
        [FromServices] IValidator<AuthService.Request.ResendOtpRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.ResendOtpAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "OTP has been resent"));
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(
        AuthService.Request.RefreshTokenRequest request,
        [FromServices] IValidator<AuthService.Request.RefreshTokenRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        var response = await _service.RefreshTokenAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(response, "Token refreshed successfully"));
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        AuthService.Request.ResetPasswordRequest request,
        [FromServices] IValidator<AuthService.Request.ResetPasswordRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        await _service.ResetPasswordAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Password reset successfully. Please sign in again"));
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
