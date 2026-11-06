using Microsoft.AspNetCore.SignalR;
using Pegaxus.Service.LocationTrackingService;

namespace Pegaxus.API.Hubs;

public sealed class SignalRLocationTrackingService(IHubContext<TransportTrackingHub, ITransportTrackingClient> hub) : ILocationTrackingRealtimeService
{
    public Task PublishAsync(Response.LocationUpdate location, IReadOnlyList<Guid> viewerAccountIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return hub.Clients.Groups(viewerAccountIds.Select(accountId => TransportTrackingHub.Group(location.TransportId, accountId)).ToArray()).LocationUpdated(location);
    }
}
