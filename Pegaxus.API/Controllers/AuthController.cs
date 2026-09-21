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

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        AuthService.RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RegisterAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            response,
            "Registration successful. Check your email for the OTP.",
            HttpContext.TraceIdentifier));
    }
}
