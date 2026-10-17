using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.LocationTrackingService;

public sealed partial class Service
{
    public async Task<Response.Accepted> RecordAsync(Guid transportId, Request.LocationRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, validator);
        var entry = state.For(transportId);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            Response.LocationUpdate point;
            await using (var transaction = await db.Database.BeginSerializableAsync(cancellationToken))
            {
                await db.LockRowsAsync(db.Transports.Where(transport => transport.TransportId == transportId), transport => transport.TransportId, cancellationToken);
                var (_, transport) = await DriverTransportAsync(transportId, accountId, cancellationToken);
                var segments = await EffectiveSegmentsAsync(transportId, transport.Status, cancellationToken);
                if (request.RoutePlanRouteId.HasValue && !segments.Any(segment => segment.Id == request.RoutePlanRouteId))
                    throw new BadRequestException("The segment must belong to the transport's effective route", "VALIDATION_ERROR");
                var recordedAt = request.RecordedAt!.Value.ToUniversalTime();
                // Delayed and duplicate samples cannot move the truck backwards or repeat geofence effects.
                if (entry.Latest is { } previous && previous.RecordedAt >= recordedAt)
                    return new(clock.GetUtcNow());
                point = new(transportId, request.RoutePlanRouteId ?? CurrentSegment(segments, transport.Status)?.Id,
                    request.Latitude!.Value, request.Longitude!.Value, request.HeadingDegrees, request.SpeedKmh, request.AccuracyMeters, recordedAt);
                var arrivedAt = Truncate(recordedAt);
                var entered = segments.Where(segment => segment.ActualDepartureAt.HasValue && segment.ActualDepartureAt <= arrivedAt &&
                    !segment.ActualArrivalAt.HasValue && segment.To.Latitude.HasValue && segment.To.Longitude.HasValue &&
                    Geo.DistanceMeters(point.Latitude, point.Longitude, segment.To.Latitude.Value, segment.To.Longitude.Value) <= options.Value.GeofenceRadiusMeters)
                    .Select(segment => segment.Id).Order().ToArray();
                foreach (var id in entered)
                {
                    await db.LockRowsAsync(db.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == id), segment => segment.RoutePlanRouteId, cancellationToken);
                    var segment = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == id, cancellationToken);
                    await db.Entry(segment).ReloadAsync(cancellationToken);
                    if (segment.ActualArrivalAt is null) { segment.ActualArrivalAt = arrivedAt; segment.UpdatedAt = Truncate(clock.GetUtcNow()); }
                }
                if (segments.LastOrDefault() is { } last && entered.Contains(last.Id) && transport.ActualArrivalAt is null)
                {
                    var tracked = await db.Transports.SingleAsync(item => item.TransportId == transportId, cancellationToken);
                    await db.Entry(tracked).ReloadAsync(cancellationToken);
                    if (tracked.ActualArrivalAt is null) { tracked.ActualArrivalAt = arrivedAt; tracked.UpdatedAt = Truncate(clock.GetUtcNow()); }
                }
                if (entered.Length > 0) await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            entry.Latest = point;
            try
            {
                var viewers = await ViewerAccountsAsync(transportId, CancellationToken.None);
                await realtime.PublishAsync(point, viewers, CancellationToken.None);
            }
            catch (Exception error) { logger.LogWarning(error, "Could not stream location for transport {TransportId}; snapshot remains available", transportId); }
            return new(clock.GetUtcNow());
        }
        finally { entry.Gate.Release(); }
    }
}
