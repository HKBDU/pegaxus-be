using System.Text.Json;
using Pegaxus.Service.Exceptions;

using BookingResponse = Pegaxus.Service.BookingService.Response;
using HealthResponse = Pegaxus.Service.HorseHealthService.Response;
using TrackingResponse = Pegaxus.Service.LocationTrackingService.Response;

namespace Pegaxus.Service.Assistant.Tools;

public sealed class CustomerGetTrackingTool(
    Pegaxus.Service.BookingService.IBookingService bookings,
    Pegaxus.Service.LocationTrackingService.ILocationTrackingService tracking,
    Pegaxus.Service.HorseHealthService.IHorseHealthService horseHealth) : IAssistantTool
{
    private static readonly HashSet<string> TrackingStatuses = new(StringComparer.Ordinal)
    {
        "READY",
        "IN_TRANSIT",
        "COMPLETED"
    };

    private sealed class Arguments
    {
        public Guid BookingId { get; init; }
    }

    public string Name => "customer_get_tracking";
    public string Description => "Get the authenticated customer's transport GPS snapshot, current route segment, and horse health.";
    public JsonElement ParametersSchema => AssistantToolJson.Schema("""
        {"type":"object","properties":{"bookingId":{"type":"string","format":"uuid"}},"required":["bookingId"],"additionalProperties":false}
        """);

    public async Task<AssistantToolResult> RunAsync(JsonElement arguments, AssistantToolContext context)
    {
        AssistantToolJson.ValidateContext(context);
        var input = AssistantToolJson.Parse<Arguments>(arguments);
        if (input.BookingId == Guid.Empty)
            throw new BadRequestException("bookingId must be a non-empty UUID.", "VALIDATION_ERROR");

        var booking = await bookings.GetAsync(context.AccountId, input.BookingId, context.CancellationToken);
        if (booking.Transport is not { } transport)
        {
            var noTransport = new TrackingToolData(booking.BookingId, booking.BookingCode, null, null, null,
                false, "No transport is assigned to this booking yet.", null, null, null, null, null, null, []);
            var bookingReference = new AssistantReferenceCandidate("booking", booking.BookingId.ToString(), booking.BookingCode, booking.UpdatedAt);
            return new(Name, noTransport, [bookingReference]);
        }

        if (!TrackingStatuses.Contains(transport.Status))
        {
            var notStarted = new TrackingToolData(booking.BookingId, booking.BookingCode,
                transport.TransportId, transport.TransportCode, transport.Status, false,
                "GPS, route segment, and horse health are available after the transport is ready to travel.",
                null, null, null, null, null, null, []);
            var earlyReference = new AssistantReferenceCandidate("tracking", transport.TransportId.ToString(), transport.TransportCode, booking.UpdatedAt);
            return new(Name, notStarted, [earlyReference]);
        }

        var snapshot = await tracking.GetAsync(transport.TransportId, context.AccountId, context.CancellationToken);
        var health = await horseHealth.GetOverviewAsync(transport.TransportId, context.AccountId, context.CancellationToken);
        var currentSegment = CurrentSegment(snapshot);
        var data = new TrackingToolData(booking.BookingId, booking.BookingCode,
            snapshot.TransportId, transport.TransportCode, snapshot.TransportStatus, true, null,
            snapshot.ServerTime,
            snapshot.Truck is null ? null : new GpsToolPoint(
                snapshot.Truck.Latitude, snapshot.Truck.Longitude, snapshot.Truck.RecordedAt,
                snapshot.Truck.AccuracyMeters, snapshot.Truck.HeadingDegrees, snapshot.Truck.SpeedKmh),
            currentSegment is null ? null : ProjectSegment(currentSegment),
            snapshot.NextStop is null ? null : new TrackingStop(snapshot.NextStop.LocationName, snapshot.NextStop.City, snapshot.NextStop.Country),
            snapshot.EtaAt, snapshot.PlannedRemainingDistanceKm,
            health.Horses.Select(ProjectHorseHealth).ToArray());
        var reference = new AssistantReferenceCandidate(
            "tracking", snapshot.TransportId.ToString(), transport.TransportCode, snapshot.ServerTime);
        return new(Name, data, [reference]);
    }

    private static TrackingResponse.Segment? CurrentSegment(TrackingResponse.Snapshot snapshot)
    {
        var segments = snapshot.PlannedRoute.Segments;
        return segments.FirstOrDefault(segment => segment.Status == "IN_TRANSIT")
            ?? (snapshot.TransportStatus switch
            {
                "READY" => segments.FirstOrDefault(),
                "COMPLETED" => segments.LastOrDefault(),
                _ => null
            });
    }

    private static CurrentSegmentToolData ProjectSegment(TrackingResponse.Segment segment) => new(
        segment.RoutePlanRouteId, segment.DisplayOrder, segment.Status, segment.TransportMode,
        new TrackingStop(segment.FromLocation.LocationName, segment.FromLocation.City, segment.FromLocation.Country),
        new TrackingStop(segment.ToLocation.LocationName, segment.ToLocation.City, segment.ToLocation.Country));

    private static HorseHealthToolItem ProjectHorseHealth(HealthResponse.HorseOverview horse) => new(
        horse.Horse.HorseId, horse.Horse.HorseName, horse.LatestHealthStatus,
        horse.LatestLog?.HydrationStatus, horse.LatestLog?.CheckedAt,
        horse.LatestLog?.VeterinarianRequired, horse.DetachedAt);
}

public sealed record TrackingToolData(Guid BookingId, string BookingCode, Guid? TransportId, string? TransportCode,
    string? TransportStatus, bool TrackingAvailable, string? AvailabilityMessage, DateTimeOffset? SnapshotAt,
    GpsToolPoint? Gps, CurrentSegmentToolData? CurrentSegment, TrackingStop? NextStop,
    DateTimeOffset? EtaAt, decimal? PlannedRemainingDistanceKm, IReadOnlyList<HorseHealthToolItem> HorseHealth);

public sealed record GpsToolPoint(decimal Latitude, decimal Longitude, DateTimeOffset RecordedAt,
    decimal? AccuracyMeters, decimal? HeadingDegrees, decimal? SpeedKmh);

public sealed record CurrentSegmentToolData(Guid SegmentId, int DisplayOrder, string Status,
    string TransportMode, TrackingStop From, TrackingStop To);

public sealed record TrackingStop(string Name, string? City, string Country);

public sealed record HorseHealthToolItem(Guid HorseId, string HorseName, string? HealthStatus,
    string? HydrationStatus, DateTimeOffset? LastCheckedAt, bool? VeterinarianRequired, DateTimeOffset? DetachedAt);
