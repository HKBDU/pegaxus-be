using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pegaxus.Repository.Enum;
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
            Response.Arrival? arrival = null;
            await using (var transaction = await db.Database.BeginSerializableAsync(cancellationToken))
            {
                await db.LockRowsAsync(db.Transports.Where(transport => transport.TransportId == transportId), transport => transport.TransportId, cancellationToken);
                var (actor, transport) = await DriverTransportAsync(transportId, accountId, cancellationToken);
                var segments = await EffectiveSegmentsAsync(transportId, transport.Status, cancellationToken);
                if (request.RoutePlanRouteId.HasValue && !segments.Any(segment => segment.Id == request.RoutePlanRouteId))
                    throw new BadRequestException("The segment must belong to the transport's effective route", "VALIDATION_ERROR");
                // Only the driver of the segment in transit tracks the truck. A previous or next driver whose app is still running is acknowledged but
                // ignored, so two phones cannot make the truck jump between two places. The signed handover moves the turn to the next driver.
                if (CurrentSegment(segments, transport.Status)?.DriverEmployeeId != actor.EmployeeId)
                    return new(clock.GetUtcNow(), false, "NOT_CURRENT_SEGMENT_DRIVER");
                var recordedAt = request.RecordedAt!.Value.ToUniversalTime();
                // Delayed and duplicate samples cannot move the truck backwards or repeat geofence effects.
                if (entry.Latest is { } previous && previous.RecordedAt >= recordedAt)
                    return new(clock.GetUtcNow());
                point = new(transportId, request.RoutePlanRouteId ?? CurrentSegment(segments, transport.Status)?.Id,
                    request.Latitude!.Value, request.Longitude!.Value, request.HeadingDegrees, request.SpeedKmh, request.AccuracyMeters, recordedAt);
                var arrivedAt = Truncate(recordedAt);
                var entered = Enumerable.Range(0, segments.Count).Where(index => segments[index].ActualDepartureAt.HasValue && segments[index].ActualDepartureAt <= arrivedAt &&
                    !segments[index].ActualArrivalAt.HasValue && segments[index].To.Latitude.HasValue && segments[index].To.Longitude.HasValue &&
                    Geo.DistanceMeters(point.Latitude, point.Longitude, segments[index].To.Latitude!.Value, segments[index].To.Longitude!.Value) <= options.Value.GeofenceRadiusMeters).ToArray();
                var wrote = false;
                foreach (var index in entered)
                {
                    var next = index + 1 < segments.Count ? segments[index + 1] : null;
                    var required = next is null || HandoverRules.RequiredBetween(segments[index].DriverEmployeeId, next.DriverEmployeeId);
                    // Only a boundary without a handover takes its time from GPS (M3); otherwise the signed record decides.
                    if (!required)
                    {
                        foreach (var id in new[] { segments[index].Id, next!.Id }.Order())
                            await db.LockRowsAsync(db.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == id), segment => segment.RoutePlanRouteId, cancellationToken);
                        var from = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == segments[index].Id, cancellationToken);
                        var to = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == next.Id, cancellationToken);
                        await db.Entry(from).ReloadAsync(cancellationToken);
                        await db.Entry(to).ReloadAsync(cancellationToken);
                        if (from.ActualArrivalAt is null)
                        {
                            from.ActualArrivalAt = arrivedAt; from.UpdatedAt = Truncate(clock.GetUtcNow());
                            if (to.ActualDepartureAt is null) { to.ActualDepartureAt = arrivedAt; to.UpdatedAt = from.UpdatedAt; }
                            wrote = true;
                        }
                    }
                    arrival ??= await ArrivalAsync(transportId, segments, index, required ? null : arrivedAt, required, cancellationToken);
                }
                if (wrote) await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            entry.Latest = point;
            try
            {
                var viewers = await ViewerAccountsAsync(transportId, CancellationToken.None);
                await realtime.PublishAsync(point, viewers, CancellationToken.None);
            }
            catch (Exception error) { logger.LogWarning(error, "Could not stream location for transport {TransportId}; snapshot remains available", transportId); }
            return new(clock.GetUtcNow(), true, null, arrival);
        }
        finally { entry.Gate.Release(); }
    }

    private async Task<Response.Arrival> ArrivalAsync(Guid transportId, IReadOnlyList<SegmentData> segments, int index, DateTime? arrivalAt, bool required, CancellationToken cancellationToken)
    {
        var from = segments[index];
        var nextId = index + 1 < segments.Count ? segments[index + 1].Id : (Guid?)null;
        var existing = required ? await db.HandoverRecords.AsNoTracking().Where(record => record.TransportId == transportId && record.FromRoutePlanRouteId == from.Id &&
                record.ToRoutePlanRouteId == nextId && record.Status != HandoverStatus.CANCELLED)
            .OrderByDescending(record => record.SequenceNo).Select(record => new Response.ExistingHandover(record.HandoverRecordId, record.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken) : null;
        return new(index + 2, from.Id, nextId, arrivalAt is null ? null : AsUtc(arrivalAt), required,
            new(from.To.LocationName, from.To.Latitude, from.To.Longitude), options.Value.GeofenceRadiusMeters, existing);
    }
}
