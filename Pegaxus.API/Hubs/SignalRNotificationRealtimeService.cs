using Microsoft.AspNetCore.SignalR;
using Pegaxus.Service.NotificationService;

namespace Pegaxus.API.Hubs;

public sealed class SignalRNotificationRealtimeService
    : INotificationRealtimeService
{
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext;
    private readonly ILogger<SignalRNotificationRealtimeService> _logger;

    public SignalRNotificationRealtimeService(
        IHubContext<NotificationHub, INotificationClient> hubContext,
        ILogger<SignalRNotificationRealtimeService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendToAccountAsync(
        Guid accountId,
        NotificationRealtimeDto notification,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.User(accountId.ToString()).ReceiveNotification(notification);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to push notification {NotificationId} to account {AccountId}",
                notification.Id,
                accountId);
            throw;
        }
    }
}
