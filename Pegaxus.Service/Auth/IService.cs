namespace Pegaxus.Service.Auth;

public interface IService
{
    Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);
}
