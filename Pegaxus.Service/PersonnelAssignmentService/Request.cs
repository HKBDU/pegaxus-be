using System.Text.Json.Serialization;

namespace Pegaxus.Service.PersonnelAssignmentService;

public static class Request
{
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
