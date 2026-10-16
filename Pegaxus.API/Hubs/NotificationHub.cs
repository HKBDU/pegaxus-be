using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.API.Hubs;

[Authorize]
public sealed class NotificationHub : Hub<INotificationClient>
{
    private readonly INotificationService _notificationService;

    public NotificationHub(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task AcknowledgeDelivered(Guid notificationId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var accountId))
            throw new HubException("The authenticated account identifier is invalid");

        try
        {
            await _notificationService.AcknowledgeDeliveredAsync(accountId, notificationId, Context.ConnectionAborted);
        }
        catch (NotFoundException)
        {
            throw new HubException("NOTIFICATION_NOT_FOUND: Notification not found");
        }
    }
}
