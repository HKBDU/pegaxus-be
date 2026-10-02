using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Pegaxus.API.Hubs;

public sealed class AccountUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? connection.User?.FindFirstValue("sub")
            ?? connection.User?.FindFirstValue("accountId");
    }
}
