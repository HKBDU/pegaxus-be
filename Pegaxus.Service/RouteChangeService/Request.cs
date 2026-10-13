using Pegaxus.Repository.Entities;

namespace Pegaxus.Service.RouteChangeService;

public static class Request
{
    public sealed class RouteChangeRequest
    {
        public Guid RouteChangeRequestId { get; set; }
        public Guid RoutePlanId { get; set; }
        public Guid? BackupRoutePlanId { get; set; }
        public string Status { get; set; } = "PENDING";
        public string Reason { get; set; } = null!;
        public Guid? ClientRequestId { get; set; }
        public string SubmittedRequest { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime NewEstimatedArrivalAt { get; set; }
        public decimal? EstimatedExtraCost { get; set; }
        public string? Currency { get; set; }
        public string CurrentSegments { get; set; } = null!;
        public string ProposedSegments { get; set; } = null!;
        public string Comparison { get; set; } = null!;
        public string[] ApprovalReasons { get; set; } = [];
        public Guid RequestedByEmployeeId { get; set; }
        public Guid? DecidedByEmployeeId { get; set; }
        public string? DecisionNote { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionRequest { get; set; }
        public string? DecisionResponse { get; set; }
        public Guid? IncidentReportId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public RoutePlan RoutePlan { get; set; } = null!;
        public Employee RequestedByEmployee { get; set; } = null!;
    }

    public sealed class CreateRequest
    {
        public string Reason { get; set; } = "";
        public DateTimeOffset NewEstimatedArrivalAt { get; set; }
        public decimal? EstimatedExtraCost { get; set; }
        public string? Currency { get; set; }
        public string? Note { get; set; }
    }

    public sealed class DecisionRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string Decision { get; set; } = "";
        public string? Note { get; set; }
    }

    public sealed class ActivationRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public List<SegmentRequest>? Segments { get; set; }
        public string? Note { get; set; }
    }

    public sealed class SegmentRequest
    {
        public Guid RoutePlanRouteId { get; set; }
        public List<Guid>? VehicleIds { get; set; }
        public OutsourcingRequest? Outsourcing { get; set; }
    }

    public sealed class OutsourcingRequest
    {
        public Guid TransportProviderId { get; set; }
        public string? Note { get; set; }
    }

    public sealed class DriverNoticeRequest
    {
        public string? Message { get; set; }
    }
}
