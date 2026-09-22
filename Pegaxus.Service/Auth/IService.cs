namespace Pegaxus.Service.Auth;

public interface IService
{
    Task<Response.RegisterResponse> RegisterAsync(Request.RegisterRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(Request.ForgotPasswordRequest request, CancellationToken cancellationToken = default);
}
