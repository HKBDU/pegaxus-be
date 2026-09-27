using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Models;
using Pegaxus.Service.Exceptions;
using System.Security.Claims;

using AuthService = Pegaxus.Service.Auth;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService.IService _service;
    private readonly IConfiguration _configuration;
    private const string GoogleExternalScheme = "GoogleExternal";

    public AuthController(AuthService.IService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    [HttpGet("google/login")]
    [AllowAnonymous]
    public IActionResult GoogleLogin()
    {
        if (string.IsNullOrWhiteSpace(_configuration["Google:ClientId"]) ||
            string.IsNullOrWhiteSpace(_configuration["Google:ClientSecret"]))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ResponseBuilder.ErrorResponse(
                null, "Google login is not configured", "GOOGLE_NOT_CONFIGURED",
                StatusCodes.Status503ServiceUnavailable, HttpContext.TraceIdentifier));

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.ActionLink(nameof(GoogleComplete))
        };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleComplete(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration["Google:ClientId"]) ||
            string.IsNullOrWhiteSpace(_configuration["Google:ClientSecret"]))
            return RedirectGoogleError("GOOGLE_NOT_CONFIGURED");

        var result = await HttpContext.AuthenticateAsync(GoogleExternalScheme);
        if (!result.Succeeded || result.Principal is null)
            return RedirectGoogleError("GOOGLE_AUTHENTICATION_FAILED");

        var principal = result.Principal;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            return RedirectGoogleError("GOOGLE_ACCOUNT_INVALID");

        try
        {
            var response = await _service.GoogleLoginAsync(new AuthService.Request.GoogleLoginRequest
            {
                Subject = subject,
                Email = email,
                FullName = principal.FindFirstValue(ClaimTypes.Name) ?? email,
                AvatarUrl = principal.FindFirstValue("picture")
            }, cancellationToken);

            var code = await _service.CreateGoogleLoginCodeAsync(response, cancellationToken);
            await HttpContext.SignOutAsync(GoogleExternalScheme);
            return Redirect($"{GetFrontendCallbackUrl()}?code={Uri.EscapeDataString(code)}");
        }
        catch (Exceptions exception)
        {
            await HttpContext.SignOutAsync(GoogleExternalScheme);
            return RedirectGoogleError(exception.ErrorCode);
        }
    }

    [HttpPost("google/exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> ExchangeGoogleLogin(
        AuthService.Request.GoogleLoginExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ExchangeGoogleLoginCodeAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response, "Google login successful. Welcome back!", traceId: HttpContext.TraceIdentifier));
    }

    private string GetFrontendCallbackUrl()
    {
        var frontendUrl = _configuration["Cors:FrontendUrl"] ?? "http://localhost:5173";
        return $"{frontendUrl.TrimEnd('/')}/auth/google/callback";
    }

    private IActionResult RedirectGoogleError(string errorCode) =>
        Redirect($"{GetFrontendCallbackUrl()}?error={Uri.EscapeDataString(errorCode)}");

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

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        AuthService.Request.LogoutRequest request,
        [FromServices] IValidator<AuthService.Request.LogoutRequest> validator,
        CancellationToken cancellationToken)
    {
        var error = await ValidateRequestAsync(request, validator, cancellationToken);
        if (error is not null) return error;
        await _service.LogoutAsync(request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(null, "Logout successful"));
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
