namespace Pegaxus.Service.AccountService;

public interface IService
{
    Task<Response.ProfileResponse> GetCurrentProfileAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Response.ProfileResponse> UpdateProfileAsync(Guid accountId, Request.UpdateProfileRequest request, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(Guid accountId, Request.ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
