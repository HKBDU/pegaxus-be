namespace Pegaxus.Service.LocationTrackingService;

public static class Response
{
    public sealed record LocationBrief(Guid LocationId, string LocationName, string? LocationType, string? City, string Country,
        decimal? Latitude, decimal? Longitude);
    public sealed record Segment(Guid RoutePlanRouteId, int DisplayOrder, string Status, string TransportMode,
        LocationBrief FromLocation, LocationBrief ToLocation);
    public sealed record PlannedRoute(IReadOnlyList<Segment> Segments);
    public sealed record Truck(decimal Latitude, decimal Longitude, decimal? HeadingDegrees, decimal? SpeedKmh,
        decimal? AccuracyMeters, DateTimeOffset RecordedAt, Guid? RoutePlanRouteId);
    public sealed record LocationUpdate(Guid TransportId, Guid? RoutePlanRouteId, decimal Latitude, decimal Longitude,
        decimal? HeadingDegrees, decimal? SpeedKmh, decimal? AccuracyMeters, DateTimeOffset RecordedAt);
    public sealed record Snapshot(DateTimeOffset ServerTime, Guid TransportId, string TransportStatus, PlannedRoute PlannedRoute,
        LocationBrief? NextStop, DateTimeOffset? EtaAt, decimal PlannedRemainingDistanceKm, Truck? Truck);
    public sealed record Accepted(DateTimeOffset ServerTime);
    public sealed record PermissionDenied(bool Notified);
}
