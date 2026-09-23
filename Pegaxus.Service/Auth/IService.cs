namespace Pegaxus.Service.Auth;

public interface IService
{
    Task<Response.RegisterResponse> RegisterAsync(Request.RegisterRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(Request.ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task VerifyEmailAsync(Request.VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<Response.ResendOtpResponse> ResendOtpAsync(Request.ResendOtpRequest request, CancellationToken cancellationToken = default);
    Task<Response.RefreshTokenResponse> RefreshTokenAsync(Request.RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Request.ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
