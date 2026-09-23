using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Models;

using AccountService = Pegaxus.Service.AccountService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly AccountService.IService _service;

    public AccountsController(AccountService.IService service)
    {
        _service = service;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentProfile(CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                HttpContext.TraceIdentifier));

        var response = await _service.GetCurrentProfileAsync(accountId, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Profile retrieved successfully",
            HttpContext.TraceIdentifier));
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateCurrentProfile([FromForm] AccountService.Request.UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                HttpContext.TraceIdentifier));

        var response = await _service.UpdateProfileAsync(accountId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Profile updated successfully",
            HttpContext.TraceIdentifier));
    }

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword(AccountService.Request.ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var accountIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");

        if (!Guid.TryParse(accountIdValue, out var accountId))
            return Unauthorized(ResponseBuilder.ErrorResponse(
                null,
                "The authenticated account identifier is invalid",
                HttpContext.TraceIdentifier));

        await _service.ChangePasswordAsync(accountId, request, cancellationToken);

        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Change password successfully",
            HttpContext.TraceIdentifier));
    }
}
