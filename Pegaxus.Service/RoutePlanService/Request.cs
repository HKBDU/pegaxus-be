namespace Pegaxus.Service.RoutePlanService;

public static class Request
{
    public sealed class LegalReviewRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public bool IsFeasible { get; set; }
        public string? Note { get; set; }
        public List<int> BlockedSegments { get; set; } = [];
    }

    public sealed class ListRequest
    {
        public string? RouteType { get; set; }
        public string? Status { get; set; }
        public bool IncludeCancelled { get; set; }
    }

    public sealed class SaveRequest
    {
        public string RouteType { get; set; } = string.Empty;
        public List<SegmentRequest> Segments { get; set; } = [];
        public string? RouteNote { get; set; }
        public string Action { get; set; } = string.Empty;
        public Guid? IncidentReportId { get; set; }
    }

    public sealed class UpdateRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string UpdateReason { get; set; } = string.Empty;
        public List<SegmentRequest> Segments { get; set; } = [];
        public string? RouteNote { get; set; }
        public string Action { get; set; } = "SAVE_DRAFT";
    }

    public sealed class CancelRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class SegmentRequest
    {
        public int SequenceNo { get; set; }
        public Guid? RouteId { get; set; }
        public Guid? FromLocationId { get; set; }
        public Guid? ToLocationId { get; set; }
        public string? RouteType { get; set; }
        public string? TransportMode { get; set; }
        public DateTimeOffset PlannedDepartureAt { get; set; }
        public DateTimeOffset PlannedArrivalAt { get; set; }
        public List<Guid> VehicleIds { get; set; } = [];
        public OutsourcingRequest? Outsourcing { get; set; }
        public string? Note { get; set; }
    }

    public sealed class OutsourcingRequest
    {
        public Guid TransportProviderId { get; set; }
        public string? Note { get; set; }
    }
}
