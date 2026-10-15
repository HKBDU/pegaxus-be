using System.Security.Claims;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Reads the authenticated account identifier from the access token claims.</summary>
    /// <exception cref="UnauthorizedException">The token carries no valid account identifier.</exception>
    public static Guid GetAccountId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue("accountId");

        return Guid.TryParse(value, out var accountId)
            ? accountId
            : throw new UnauthorizedException("The authenticated account identifier is invalid");
    }
}
