using Pegaxus.Service.NotificationService;

namespace Pegaxus.API.Hubs;

public interface INotificationClient
{
    Task ReceiveNotification(NotificationRealtimeDto notification);
}
