using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.Service.Exceptions;

using NotificationService = Pegaxus.Service.NotificationService;

namespace Pegaxus.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly NotificationService.INotificationService _service;

    public NotificationsController(NotificationService.INotificationService service)
    {
        _service = service;
    }

    /// <summary>
    /// List account notifications.
    /// </summary>
    /// <remarks>Returns notifications belonging to the authenticated account.</remarks>
    /// <param name="request">The notification filters and pagination parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with a paginated notification list.</returns>
    /// <exception cref="BadRequestException">The request fields or query parameters are invalid.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] NotificationService.Request.NotificationListRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(AccountId, request, cancellationToken);
        return Success(response, "Notifications retrieved successfully");
    }

    /// <summary>
    /// Count unread notifications.
    /// </summary>
    /// <remarks>Only counts notifications belonging to the authenticated account.</remarks>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK with the unread count in value.count.</returns>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var count = await _service.GetUnreadCountAsync(AccountId, cancellationToken);
        return Success(new { Count = count }, "Unread notification count retrieved successfully");
    }

    /// <summary>
    /// Mark a notification as read.
    /// </summary>
    /// <remarks>Only notifications belonging to the authenticated account can be updated.</remarks>
    /// <param name="notificationId">The notification identifier (UUID).</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after the notification is marked as read.</returns>
    /// <exception cref="NotFoundException">The requested resource does not exist or is not visible to the caller.</exception>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpPatch("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        await _service.MarkReadAsync(AccountId, notificationId, cancellationToken);
        return Success(null, "Notification marked as read");
    }

    /// <summary>
    /// Mark all notifications as read.
    /// </summary>
    /// <remarks>Marks all unread notifications belonging to the authenticated account as read.</remarks>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>200 OK after all unread notifications are updated.</returns>
    /// <exception cref="UnauthorizedException">The authentication session or credentials are invalid.</exception>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _service.MarkAllReadAsync(AccountId, cancellationToken);
        return Success(null, "All notifications marked as read");
    }

}
