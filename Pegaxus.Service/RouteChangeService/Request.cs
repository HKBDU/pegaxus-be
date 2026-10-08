namespace Pegaxus.Service.RouteChangeService;

public static class Request
{
    public sealed class CreateRequest
    {
        public Guid? ClientRequestId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid? BackupRoutePlanId { get; set; }
        public List<SegmentRequest>? ReplacementSegments { get; set; }
        public DateTimeOffset NewEstimatedArrivalAt { get; set; }
        public decimal? EstimatedExtraCost { get; set; }
        public string? Currency { get; set; }
        public string? Note { get; set; }
    }

    public sealed class SegmentRequest
    {
        public int SequenceNo { get; set; }
        public Guid? RouteId { get; set; }
        public Guid? FromLocationId { get; set; }
        public Guid? ToLocationId { get; set; }
        public string? TransportMode { get; set; }
        public DateTimeOffset PlannedDepartureAt { get; set; }
        public DateTimeOffset PlannedArrivalAt { get; set; }
        public List<Guid>? VehicleIds { get; set; }
        public OutsourcingRequest? Outsourcing { get; set; }
        public string? Note { get; set; }
    }

    public sealed class OutsourcingRequest
    {
        public Guid TransportProviderId { get; set; }
        public string? ServiceType { get; set; }
        public string? ProviderReference { get; set; }
        public string? Note { get; set; }
    }

    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; }
        public Guid? TransportPlanId { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }

    public sealed class DecisionRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string Decision { get; set; } = string.Empty;
        public string? Note { get; set; }
        // Explicit, manager-approved return itinerary when no confirmed return backup exists.
        public List<SegmentRequest>? ReturnSegments { get; set; }
    }
}
