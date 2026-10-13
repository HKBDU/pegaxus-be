using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;

using AccountService = Pegaxus.Service.AccountService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/accounts")]
public sealed class AccountsController : ApiControllerBase
{
    private readonly AccountService.IService _service;

    public AccountsController(AccountService.IService service)
    {
        _service = service;
    }

    /// <summary>
    /// Get the current account profile.
    /// </summary>
    /// <remarks>Returns the profile associated with the authenticated account.</remarks>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the current account profile.</returns>
    /// <exception cref="KeyNotFoundException">The authenticated account no longer exists.</exception>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentProfile(CancellationToken cancellationToken)
    {
        var accountId = AccountId;

        var response = await _service.GetCurrentProfileAsync(accountId, cancellationToken);

        return Success(response, "Profile retrieved successfully");
    }

    /// <summary>
    /// Update the current account profile.
    /// </summary>
    /// <remarks>Accepts multipart/form-data. Only supplied fields are changed; profile images must be JPG or PNG and no larger than 2 MB.</remarks>
    /// <param name="request">The profile fields and optional image to update.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the updated account profile.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="KeyNotFoundException">The authenticated account no longer exists.</exception>
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateCurrentProfile([FromForm] AccountService.Request.UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var accountId = AccountId;

        var response = await _service.UpdateProfileAsync(accountId, request, cancellationToken);

        return Success(response, "Profile updated successfully");
    }

    /// <summary>
    /// Change the current account password.
    /// </summary>
    /// <remarks>Requires the current password and a valid new password with matching confirmation.</remarks>
    /// <param name="request">The current password, new password, and password confirmation.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the password is changed.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="KeyNotFoundException">The authenticated account no longer exists.</exception>
    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword(AccountService.Request.ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var accountId = AccountId;

        await _service.ChangePasswordAsync(accountId, request, cancellationToken);

        return Success(null, "Change password successfully");
    }
}
