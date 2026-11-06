namespace Pegaxus.Service.LocationTrackingService;

public static class Request
{
    public sealed class ArrivalRequest
    {
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
    }

    public sealed class LocationRequest
    {
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
        public decimal? SpeedKmh { get; set; }
        public decimal? HeadingDegrees { get; set; }
        public decimal? AccuracyMeters { get; set; }
        public Guid? RoutePlanRouteId { get; set; }
    }
}
