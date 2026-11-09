using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.RoutePlanService;

/// <summary>
/// Route plan writes run in a Serializable transaction, which PostgreSQL may abort when another writer touches the same rows at the
/// same time: GPS pings lock the transport, and two plans may book the same vehicle in one slot. The write is retried on a fresh
/// read, so the second try either succeeds or reports the real business error (for example VEHICLE_SCHEDULE_CONFLICT).
/// </summary>
public sealed partial class Service
{
    private const int WriteAttempts = 3;

    private sealed record MetricKey(Guid FromLocationId, Guid ToLocationId, TransportMode Mode);

    private IReadOnlyDictionary<MetricKey, (decimal? DistanceKm, int? DurationMinutes)> precomputedMetrics =
        new Dictionary<MetricKey, (decimal? DistanceKm, int? DurationMinutes)>();

    /// <summary>Only the transactional part is retried; it must return before anything runs after the commit.</summary>
    private async Task<T> WithWriteRetryAsync<T>(Func<Task<T>> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (Exception error) when (PostgresErrors.IsWriteConflict(error))
            {
                db.ChangeTracker.Clear();
                if (attempt < WriteAttempts) continue;
                throw new ConflictException("The route plan could not be saved because the same data was changed at the same time. Please try again",
                    "ROUTE_PLAN_CONCURRENT_UPDATE");
            }
        }
    }

    /// <summary>
    /// Asks the map provider for the legs that need distance and duration before the transaction opens, so the transaction does not stay
    /// open during HTTP calls. Anything that fails here is left to the transaction, which reports it in its usual order.
    /// </summary>
    private async Task PrecomputeMetricsAsync(IEnumerable<Request.SegmentRequest>? segments, CancellationToken cancellationToken)
    {
        var result = new Dictionary<MetricKey, (decimal? DistanceKm, int? DurationMinutes)>();
        foreach (var segment in segments ?? [])
        {
            if (segment.RouteId.HasValue || segment.FromLocationId is not { } fromId || segment.ToLocationId is not { } toId ||
                !EnumParser.TryParseDefined<TransportMode>(segment.TransportMode ?? string.Empty, out var mode))
                continue;
            var key = new MetricKey(fromId, toId, mode);
            if (result.ContainsKey(key)) continue;
            var known = await db.Routes.AsNoTracking()
                .Where(item => item.FromLocationId == fromId && item.ToLocationId == toId && item.TransportMode == mode && !item.IsDeleted)
                .Select(item => new { item.DistanceKm, item.EstimatedDurationMinutes }).FirstOrDefaultAsync(cancellationToken);
            if (known is { DistanceKm: not null, EstimatedDurationMinutes: not null }) continue;
            var from = await db.Locations.AsNoTracking().SingleOrDefaultAsync(item => item.LocationId == fromId && !item.IsDeleted, cancellationToken);
            var to = await db.Locations.AsNoTracking().SingleOrDefaultAsync(item => item.LocationId == toId && !item.IsDeleted, cancellationToken);
            if (from is null || to is null) continue;
            try
            {
                result[key] = await routeService.CalculateMetricsAsync(from, to, mode, cancellationToken);
            }
            catch (Exceptions.Exceptions)
            {
                // The transaction calls the provider again and raises the error after the role and request checks.
            }
        }
        precomputedMetrics = result;
    }

    private Task<(decimal? DistanceKm, int? DurationMinutes)> MetricsAsync(Repository.Entities.Location from, Repository.Entities.Location to,
        TransportMode mode, CancellationToken cancellationToken) =>
        precomputedMetrics.TryGetValue(new MetricKey(from.LocationId, to.LocationId, mode), out var known)
            ? Task.FromResult(known)
            : routeService.CalculateMetricsAsync(from, to, mode, cancellationToken);
}
