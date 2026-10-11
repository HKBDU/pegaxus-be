using System.Text.Json.Serialization;

namespace Pegaxus.Service.RouteChangeService;

public static class Request
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class CreateRequest
    {
        public string Reason { get; set; } = "";
        public DateTimeOffset NewEstimatedArrivalAt { get; set; }
        public decimal? EstimatedExtraCost { get; set; }
        public string? Currency { get; set; }
        public string? Note { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class DecisionRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string Decision { get; set; } = "";
        public string? Note { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ActivationRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public List<SegmentRequest>? Segments { get; set; }
        public string? Note { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SegmentRequest
    {
        public Guid RoutePlanRouteId { get; set; }
        public List<Guid>? VehicleIds { get; set; }
        public OutsourcingRequest? Outsourcing { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class OutsourcingRequest
    {
        public Guid TransportProviderId { get; set; }
        public string? Note { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class DriverNoticeRequest
    {
        public string? Message { get; set; }
    }
}
