using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.Models;
using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationService.IService _service;

    public NotificationsController(NotificationService.IService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] NotificationService.Request.NotificationListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(GetAccountId(), request, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            response,
            "Notifications retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var count = await _service.GetUnreadCountAsync(GetAccountId(), cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            new { Count = count },
            "Unread notification count retrieved successfully",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        await _service.MarkReadAsync(GetAccountId(), notificationId, cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "Notification marked as read",
            traceId: HttpContext.TraceIdentifier));
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _service.MarkAllReadAsync(GetAccountId(), cancellationToken);
        return Ok(ResponseBuilder.SuccessResponse(
            null,
            "All notifications marked as read",
            traceId: HttpContext.TraceIdentifier));
    }

    private Guid GetAccountId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("accountId");
        if (Guid.TryParse(value, out var accountId))
            return accountId;

        throw new UnauthorizedException("The authentication session is invalid or has expired");
    }
}
