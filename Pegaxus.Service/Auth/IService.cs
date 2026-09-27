namespace Pegaxus.Service.Auth;

public interface IService
{
    Task<Response.LoginResponse> LoginAsync(Request.LoginRequest request, CancellationToken cancellationToken = default);
    Task<Response.LoginResponse> GoogleLoginAsync(Request.GoogleLoginRequest request, CancellationToken cancellationToken = default);
    Task<string> CreateGoogleLoginCodeAsync(Response.LoginResponse response, CancellationToken cancellationToken = default);
    Task<Response.LoginResponse> ExchangeGoogleLoginCodeAsync(Request.GoogleLoginExchangeRequest request, CancellationToken cancellationToken = default);
    Task<Response.RegisterResponse> RegisterAsync(Request.RegisterRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(Request.ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task VerifyEmailAsync(Request.VerifyEmailRequest request, CancellationToken cancellationToken = default);
    Task<Response.ResendOtpResponse> ResendOtpAsync(Request.ResendOtpRequest request, CancellationToken cancellationToken = default);
    Task<Response.RefreshTokenResponse> RefreshTokenAsync(Request.RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(Request.LogoutRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Request.ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
