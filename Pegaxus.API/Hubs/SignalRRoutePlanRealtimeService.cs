using Microsoft.AspNetCore.SignalR;
using Pegaxus.Service.RoutePlanService;

namespace Pegaxus.API.Hubs;

public sealed class SignalRRoutePlanRealtimeService(IHubContext<NotificationHub, INotificationClient> hub, ILogger<SignalRRoutePlanRealtimeService> logger) : IRoutePlanRealtimeService
{
    public async Task PublishCancelledAsync(IReadOnlyList<Guid> recipientAccountIds, Response.RoutePlanCancelledEvent cancelled, CancellationToken cancellationToken = default)
    {
        if (recipientAccountIds.Count == 0) return;
        try
        {
            await hub.Clients.Users(recipientAccountIds.Select(accountId => accountId.ToString()).ToArray()).RoutePlanCancelled(cancelled);
        }
        catch (Exception exception)
        {
            // The cancel already committed and the stored notification still reaches the specialist, so a failed push is only logged.
            logger.LogError(exception, "Failed to push RoutePlanCancelled for route plan {RoutePlanId}", cancelled.RoutePlanId);
        }
    }
}
