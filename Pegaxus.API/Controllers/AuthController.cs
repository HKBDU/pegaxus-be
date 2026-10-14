using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Models;
using Pegaxus.Service.Exceptions;

using AuthService = Pegaxus.Service.Auth;

namespace Pegaxus.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly AuthService.IAuthService _service;
    private readonly IConfiguration _configuration;
    private const string GoogleExternalScheme = "GoogleExternal";

    public AuthController(AuthService.IAuthService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    /// <summary>
    /// Start Google sign-in.
    /// </summary>
    /// <remarks>Challenges the browser with Google OAuth. Returns 503 if Google authentication is not configured.</remarks>
    /// <returns>A Google authentication challenge or a configuration error.</returns>
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

    /// <summary>
    /// Complete Google sign-in.
    /// </summary>
    /// <remarks>Uses the external authentication cookie and redirects to the frontend with a one-time exchange code or an error code.</remarks>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A redirect to the frontend Google callback.</returns>
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

    /// <summary>
    /// Exchange a Google login code for tokens.
    /// </summary>
    /// <remarks>Consumes the one-time code issued by the Google callback.</remarks>
    /// <param name="request">The one-time Google login code.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with access and refresh tokens.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
    [HttpPost("google/exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> ExchangeGoogleLogin(
        AuthService.Request.GoogleLoginExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.ExchangeGoogleLoginCodeAsync(request, cancellationToken);
        return Success(response, "Google login successful. Welcome back!");
    }

    private string GetFrontendCallbackUrl()
    {
        var frontendUrl = _configuration["Cors:FrontendUrl"] ?? "http://localhost:5173";
        return $"{frontendUrl.TrimEnd('/')}/auth/google/callback";
    }

    private IActionResult RedirectGoogleError(string errorCode) =>
        Redirect($"{GetFrontendCallbackUrl()}?error={Uri.EscapeDataString(errorCode)}");

    /// <summary>
    /// Sign in with email and password.
    /// </summary>
    /// <remarks>Returns access and refresh tokens for an active, verified account. Invalid request fields return 400.</remarks>
    /// <param name="request">The account email and password.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the authenticated account and tokens.</returns>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    /// <exception cref="ForbiddenException">The caller is not permitted to access the resource or perform this operation.</exception>
    /// <exception cref="TooManyRequestsException">Too many failed login attempts have been recorded.</exception>
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
        return Success(response, "Login successful. Welcome back!");
    }

    /// <summary>
    /// Register a customer account.
    /// </summary>
    /// <remarks>Creates a customer account and sends an email verification OTP. Invalid request fields return 400.</remarks>
    /// <param name="request">The customer registration details and password confirmation.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>201 Created with the new account identifiers.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Request a password reset OTP.
    /// </summary>
    /// <remarks>Returns the same success message whether or not the email exists to avoid exposing account registration.</remarks>
    /// <param name="request">The email address that should receive a password reset OTP.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK acknowledging the request.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Verify an account email.
    /// </summary>
    /// <remarks>Checks the email verification OTP before marking the account email as verified.</remarks>
    /// <param name="request">The account email and verification OTP.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after email verification.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Resend an authentication OTP.
    /// </summary>
    /// <remarks>Supports email verification and password reset purposes. Requests are subject to a resend cooldown.</remarks>
    /// <param name="request">The account email and OTP purpose.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the OTP expiration time.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Refresh authentication tokens.
    /// </summary>
    /// <remarks>Rotates a valid refresh token and issues a new access token.</remarks>
    /// <param name="request">The refresh token to rotate.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with replacement access and refresh tokens.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Reset an account password.
    /// </summary>
    /// <remarks>Validates the password reset OTP before updating the password.</remarks>
    /// <param name="request">The account email, OTP, new password, and confirmation.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the password is reset.</returns>
    /// <exception cref="Pegaxus.Service.Exceptions.Exceptions">A business or storage error occurs; its HTTP status and errorCode describe the failure.</exception>
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

    /// <summary>
    /// Sign out the current account.
    /// </summary>
    /// <remarks>Revokes the submitted refresh token for the authenticated account.</remarks>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="validator">The injected validator for the request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after sign-out.</returns>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
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
