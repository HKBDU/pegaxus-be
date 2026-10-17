using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pegaxus.API.Extensions;
using Pegaxus.Service.LocationTrackingService;

namespace Pegaxus.API.Hubs;

public interface ITransportTrackingClient
{
    Task LocationUpdated(Response.LocationUpdate location);
}

[Authorize(Policy = JwtExtensions.TransportTrackingViewerPolicy)]
public sealed class TransportTrackingHub(ILocationTrackingService service) : Hub<ITransportTrackingClient>
{
    public async Task JoinTransport(Guid transportId)
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var accountId))
            throw new HubException("UNAUTHORIZED: The authenticated account identifier is invalid");
        try
        {
            await service.EnsureViewerAsync(transportId, accountId, Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(transportId, accountId), Context.ConnectionAborted);
        }
        catch (Pegaxus.Service.Exceptions.Exceptions error)
        {
            throw new HubException($"{error.ErrorCode}: {error.Message}");
        }
    }

    public Task LeaveTransport(Guid transportId) => Guid.TryParse(Context.UserIdentifier, out var accountId)
        ? Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(transportId, accountId), Context.ConnectionAborted)
        : throw new HubException("UNAUTHORIZED: The authenticated account identifier is invalid");

    internal static string Group(Guid transportId, Guid accountId) => $"transport:{transportId:D}:account:{accountId:D}";
}
