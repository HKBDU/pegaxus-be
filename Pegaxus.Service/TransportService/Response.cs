using System.Text.Json.Serialization;

namespace Pegaxus.Service.TransportService;

public static class Response
{
    public sealed record ListResponse(DateTimeOffset ServerTime, IReadOnlyList<OngoingItem> Items,
        int PageIndex, int PageSize, int TotalItems, int TotalPages);

    public sealed record OngoingItem(Guid TransportId, string TransportCode, Guid BookingId, string BookingCode,
        string CustomerName, string Status, string? RouteType, SegmentBrief? CurrentSegment,
        int HorseCount, int ActiveIncidentCount, DateTimeOffset? DepartureAt);

    public sealed record AssignedResponse(DateTimeOffset ServerTime, IReadOnlyList<AssignedItem> Items);

    public sealed record AssignedItem(Guid TransportId, string TransportCode, string BookingCode, string Status,
        string MyRole, SegmentBrief? CurrentSegment, int HorseCount, DateTimeOffset? DepartureAt, DateTimeOffset? EtaAt);

    public sealed record SegmentBrief(Guid RoutePlanRouteId, int DisplayOrder, string FromLocationName, string ToLocationName, string Status);

    public sealed record OverviewResponse(DateTimeOffset ServerTime, TransportBrief Transport, BookingBrief Booking,
        string? RouteType, RoutePlanBrief? RoutePlan, TrackingSegment? CurrentSegment, int TotalSegments, int CompletedSegments,
        DateTimeOffset? DepartureAt, DateTimeOffset? EtaAt, int HorseCount, HorseHealthSummary HorseHealthSummary,
        IReadOnlyList<DossierBrief> ComplianceDossiers, IReadOnlyList<ActiveIncident> ActiveIncidents, bool CanViewGpsTab);

    public sealed record TransportBrief(Guid TransportId, string TransportCode, string Status,
        DateTimeOffset? ActualDepartureAt, DateTimeOffset? ActualArrivalAt, DateTimeOffset? CompletedAt);

    public sealed record BookingBrief(Guid BookingId, string BookingCode, string CustomerName,
        string OriginAddress, string OriginCountry, string DestinationAddress, string DestinationCountry);

    public sealed record RoutePlanBrief(Guid RoutePlanId, string RoutePlanCode, string Status);
    public sealed record HorseHealthSummary(int Total, int Normal, int ObservationRequired, int AtRisk, int Critical, int NoData);
    public sealed record DossierBrief(Guid ComplianceDossierId, string DossierCode, string? DossierType, string Status);

    public sealed record ActiveIncident(Guid IncidentReportId, string IncidentCode, string Category, string Status,
        string Title, DateTimeOffset OccurredAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Severity,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

    public sealed record SegmentsResponse(DateTimeOffset ServerTime, Guid TransportId, string TransportStatus,
        IReadOnlyList<TrackingSegment> Segments, IReadOnlyList<HandoverBoundary> HandoverBoundaries);

    public sealed record TrackingSegment
    {
        public Guid RoutePlanRouteId { get; init; }
        public Guid RoutePlanId { get; init; }
        public required string RoutePlanCode { get; init; }
        public int SequenceNo { get; init; }
        public int DisplayOrder { get; init; }
        public required string RouteType { get; init; }
        public required string TransportMode { get; init; }
        public required LocationBrief FromLocation { get; init; }
        public required LocationBrief ToLocation { get; init; }
        public decimal? DistanceKm { get; init; }
        public required string Status { get; init; }
        public bool IsCurrent { get; init; }
        public DateTimeOffset? PlannedDepartureAt { get; init; }
        public DateTimeOffset? PlannedArrivalAt { get; init; }
        public DateTimeOffset? ActualDepartureAt { get; init; }
        public DateTimeOffset? ActualArrivalAt { get; init; }
        public CrewMember? Driver { get; init; }
        public CrewMember? Escort { get; init; }
        public bool IsOutsourced { get; init; }
        public ProviderBrief? Provider { get; init; }
        public required IReadOnlyList<VehicleBrief> Vehicles { get; init; }
        public string? Note { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed record LocationBrief(Guid LocationId, string LocationName, string? LocationType, string? City,
        string Country, decimal? Latitude, decimal? Longitude);
    public sealed record CrewMember(Guid EmployeeId, string EmployeeCode, string FullName, string Role, string? Phone);
    public sealed record ProviderBrief(Guid TransportProviderId, string ProviderName, string? ServiceType, string? ProviderReference);
    public sealed record VehicleBrief(Guid VehicleId, string VehicleCode, string? LicensePlate);

    public sealed record HandoverBoundary(int BoundaryNo, Guid? FromRoutePlanRouteId, Guid? ToRoutePlanRouteId,
        HandoverLocation Location, bool HandoverRequired, DateTimeOffset? ArrivalAt, IReadOnlyList<string> AllowedHandoverTypes,
        HandoverBrief? ExistingHandover, bool CanCreate, IReadOnlyList<string> CreateModes);
    public sealed record HandoverLocation(string LocationName, string? Address, string Country);
    public sealed record HandoverBrief(Guid HandoverRecordId, string HandoverCode, string HandoverType, string Status);
}
