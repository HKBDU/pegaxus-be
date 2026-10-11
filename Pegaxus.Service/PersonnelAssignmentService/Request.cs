using System.Text.Json.Serialization;

namespace Pegaxus.Service.PersonnelAssignmentService;

public static class Request
{
    public sealed class AssignableRequest
    {
        public string Role { get; set; } = "";
        public DateTimeOffset? AvailableFrom { get; set; }
        public DateTimeOffset? AvailableTo { get; set; }
        public string? Search { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class AssignRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        [JsonRequired]
        public List<SegmentRequest> Segments { get; set; } = [];
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class SegmentRequest
    {
        public Guid RoutePlanRouteId { get; set; }
        public Guid? DriverEmployeeId { get; set; }
        public Guid? EscortEmployeeId { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ReplaceRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public Guid? DriverEmployeeId { get; set; }
        public Guid? EscortEmployeeId { get; set; }
        public string Reason { get; set; } = "";
    }
}
