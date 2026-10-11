namespace Pegaxus.Service.RoutePlanService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<SummaryResponse> Items { get; init; }
    }

    public sealed class SummaryResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string RoutePlanCode { get; init; }
        public Guid TransportPlanId { get; init; }
        public required string RouteType { get; init; }
        public required string Status { get; init; }
        public decimal TotalDistanceKm { get; init; }
        public int EstimatedDurationMinutes { get; init; }
        public int SegmentCount { get; init; }
        public bool IsPersonnelComplete { get; init; }
        public SequenceRangeResponse? ReplacesSequenceNo { get; init; }
        public string? RouteNote { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class SequenceRangeResponse
    {
        public int From { get; init; }
        public int To { get; init; }
    }

    public sealed class UpdateResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string Status { get; init; }
        public decimal TotalDistanceKm { get; init; }
        public int EstimatedDurationMinutes { get; init; }
        public int SegmentCount { get; init; }
        public bool IsPersonnelComplete { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class CancelResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string Status { get; init; }
        public DateTimeOffset CancelledAt { get; init; }
        public required string BookingStatus { get; init; }
    }

    public sealed class DetailResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string RoutePlanCode { get; init; }
        public Guid TransportPlanId { get; init; }
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string RouteType { get; init; }
        public required string Status { get; init; }
        public bool IsCrossBorder { get; init; }
        public decimal TotalDistanceKm { get; init; }
        public int EstimatedDurationMinutes { get; init; }
        public DateTimeOffset? PlannedDepartureAt { get; init; }
        public DateTimeOffset? PlannedArrivalAt { get; init; }
        public required IReadOnlyList<SegmentResponse> Segments { get; init; }
        public bool IsPersonnelComplete { get; init; }
        public LegalReviewResponse? LegalReview { get; init; }
        public string? RouteNote { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class SegmentResponse
    {
        public Guid RoutePlanRouteId { get; init; }
        public int SequenceNo { get; init; }
        public required string RouteType { get; init; }
        public required RouteResponse Route { get; init; }
        public required LocationResponse FromLocation { get; init; }
        public required LocationResponse ToLocation { get; init; }
        public DateTimeOffset? PlannedDepartureAt { get; init; }
        public DateTimeOffset? PlannedArrivalAt { get; init; }
        public DateTimeOffset? ActualDepartureAt { get; init; }
        public DateTimeOffset? ActualArrivalAt { get; init; }
        public required string Status { get; init; }
        public int? DwellAfterMinutes { get; init; }
        public bool IsOutsourced { get; init; }
        public required IReadOnlyList<VehicleResponse> Vehicles { get; init; }
        public ProviderResponse? Outsourcing { get; init; }
        public EmployeeResponse? Driver { get; init; }
        public EmployeeResponse? Escort { get; init; }
        public string? Note { get; init; }
    }

    public sealed class RouteResponse
    {
        public Guid RouteId { get; init; }
        public required string RouteCode { get; init; }
        public string? RouteName { get; init; }
        public required string TransportMode { get; init; }
        public decimal? DistanceKm { get; init; }
        public int? EstimatedDurationMinutes { get; init; }
    }

    public sealed class LocationResponse
    {
        public Guid LocationId { get; init; }
        public required string LocationName { get; init; }
        public string? LocationType { get; init; }
        public string? City { get; init; }
        public required string Country { get; init; }
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }
    }

    public sealed class VehicleResponse
    {
        public Guid VehicleId { get; init; }
        public required string VehicleCode { get; init; }
        public string? VehicleName { get; init; }
        public string? LicensePlate { get; init; }
        public required string TransportMode { get; init; }
        public int? CapacityHorses { get; init; }
    }

    public sealed class ProviderResponse
    {
        public Guid TransportProviderId { get; init; }
        public required string ProviderCode { get; init; }
        public required string ProviderName { get; init; }
        public required string Status { get; init; }
        public required IReadOnlyList<string> SupportedModes { get; init; }
    }

    public sealed class EmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
    }

    public sealed class ExportResponse
    {
        public required byte[] Content { get; init; }
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
    }

    public sealed class LegalReviewResultResponse
    {
        public Guid RoutePlanId { get; init; }
        public required string Status { get; init; }
        public required LegalReviewResponse LegalReview { get; init; }
        public bool CustomerUploadEnabled { get; init; }
        public bool PersonnelAssignmentPhase2Enabled { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class LegalReviewResponse
    {
        public bool IsFeasible { get; init; }
        public string? Note { get; init; }
        public required IReadOnlyList<int> BlockedSegments { get; init; }
        public required ReviewedEmployeeResponse ReviewedBy { get; init; }
        public DateTimeOffset ReviewedAt { get; init; }
    }

    public sealed class ReviewedEmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
    }
}
