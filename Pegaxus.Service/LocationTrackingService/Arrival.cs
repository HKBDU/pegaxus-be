using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.LocationTrackingService.Validator;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.LocationTrackingService;

public sealed partial class Service
{
    public async Task<Response.ArrivalConfirmed> ConfirmArrivalAsync(Guid transportId, Guid routePlanRouteId, Request.ArrivalRequest request, Guid accountId, CancellationToken cancellationToken = default)
    {
        Validation.Validate(request, new ArrivalRequestValidator());
        if (routePlanRouteId == Guid.Empty) throw new BadRequestException("Segment ID must be a non-empty UUID.", "VALIDATION_ERROR");
        await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
        await db.LockRowsAsync(db.Transports.Where(transport => transport.TransportId == transportId), transport => transport.TransportId, cancellationToken);
        var (actor, transport) = await DriverTransportAsync(transportId, accountId, cancellationToken);
        var segments = await EffectiveSegmentsAsync(transportId, transport.Status, cancellationToken);
        var index = segments.ToList().FindIndex(segment => segment.Id == routePlanRouteId);
        if (index < 0) throw new NotFoundException("Segment not found in the effective route of this transport", "SEGMENT_NOT_FOUND");
        var current = segments[index];
        if (current.DriverEmployeeId != actor.EmployeeId) throw new ForbiddenException(ForbiddenMessage, "FORBIDDEN");
        if (!current.ActualDepartureAt.HasValue) throw new ConflictException("The segment has not started yet", "SEGMENT_NOT_STARTED");
        var next = index + 1 < segments.Count ? segments[index + 1] : null;
        if (next is null || HandoverRules.RequiredBetween(current.DriverEmployeeId, next.DriverEmployeeId))
            throw new UnprocessableEntityException("A handover record is required at this boundary", "HANDOVER_REQUIRED");
        // Once the truck has left the radius the GPS time (already written on entry) stays official, so a late call must not replace it.
        if (!current.To.Latitude.HasValue || !current.To.Longitude.HasValue ||
            Geo.DistanceMeters(request.Latitude!.Value, request.Longitude!.Value, current.To.Latitude.Value, current.To.Longitude.Value) > options.Value.GeofenceRadiusMeters)
            throw current.ActualArrivalAt.HasValue
                ? new ConflictException("The truck has left the arrival area; the recorded arrival time is kept", "ARRIVAL_LOCKED")
                : new UnprocessableEntityException("You are not within the arrival radius of the segment end", "NOT_IN_ARRIVAL_RADIUS");

        foreach (var id in new[] { current.Id, next.Id }.Order())
            await db.LockRowsAsync(db.RoutePlanRoutes.Where(segment => segment.RoutePlanRouteId == id), segment => segment.RoutePlanRouteId, cancellationToken);
        var from = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == current.Id, cancellationToken);
        var to = await db.RoutePlanRoutes.SingleAsync(segment => segment.RoutePlanRouteId == next.Id, cancellationToken);
        await db.Entry(from).ReloadAsync(cancellationToken);
        await db.Entry(to).ReloadAsync(cancellationToken);
        var now = Truncate(clock.GetUtcNow());
        from.ActualArrivalAt = to.ActualDepartureAt = now;
        from.UpdatedAt = to.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(index + 2, Brief(from), Brief(to));
    }

    private static Response.ArrivalSegment Brief(RoutePlanRoute segment) =>
        new(segment.RoutePlanRouteId, TransportItinerary.StatusOf(segment).ToString(), AsUtc(segment.ActualDepartureAt), AsUtc(segment.ActualArrivalAt));
}
