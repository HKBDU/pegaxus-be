using System.Security.Claims;
using System.Threading.RateLimiting;
using Pegaxus.API.Models;

namespace Pegaxus.API.Extensions;

/// <summary>Per-user request limits for the price list management API (UC-78: 30 requests a minute, UC-79: 60 requests a minute).</summary>
public static class RateLimitingExtensions
{
    public const string PriceListPolicy = "PriceListManagement";
    public const string PriceListItemPolicy = "PriceListItemManagement";

    public static IServiceCollection AddPriceListRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PriceListPolicy, context => PerUser(context, 30));
            options.AddPolicy(PriceListItemPolicy, context => PerUser(context, 60));
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await http.Response.WriteAsJsonAsync(ResponseBuilder.ErrorResponse(null, "Too many requests. Please try again later.",
                    "TOO_MANY_REQUESTS", StatusCodes.Status429TooManyRequests, http.TraceIdentifier), cancellationToken);
            };
        });

    private static RateLimitPartition<string> PerUser(HttpContext context, int limit) => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub")
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
