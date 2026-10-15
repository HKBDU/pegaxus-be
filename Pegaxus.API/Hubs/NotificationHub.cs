using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
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

    public Task AcknowledgeDelivered(Guid notificationId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var accountId))
            throw new HubException("The authenticated account identifier is invalid");

        return _notificationService.AcknowledgeDeliveredAsync(accountId, notificationId);
    }
}
