using System.Text.Json.Serialization;

namespace Pegaxus.Service.HandoverService;

public static class Response
{
    public sealed record ListItem(Guid HandoverRecordId, string HandoverCode, int SequenceNo, string HandoverType, string Status,
        string? FromPartyName, string? ToPartyName, string? LocationName, DateTimeOffset? PlannedHandoverAt, DateTimeOffset? ActualHandoverAt);
    public sealed record ListResponse(DateTimeOffset ServerTime, IReadOnlyList<ListItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record TransportBrief(Guid TransportId, string TransportCode, string Status);
    public sealed record Party(string Type, Guid? CustomerId, Guid? EmployeeId, Guid? TransportProviderId, string? PartyName, string? ContactName, string? ContactPhone);
    public sealed record SegmentBrief(Guid RoutePlanRouteId, int DisplayOrder, string FromLocationName, string ToLocationName);
    public sealed record Location(string? LocationName, string? Address, string? Country);
    public sealed record DetailResponse(Guid HandoverRecordId, string HandoverCode, TransportBrief Transport, Guid BookingId, int SequenceNo,
        string HandoverType, string Status, DateTimeOffset? PlannedHandoverAt, DateTimeOffset? ActualHandoverAt, DateTimeOffset? CompletedAt,
        Party From, Party To, SegmentBrief? FromSegment, SegmentBrief? ToSegment, Location Location,
        string? HorseConditionNote, string? DocumentHandoverNote, string? EquipmentHandoverNote, string? DiscrepancyNote,
        string? SenderSignatureUrl, string? ReceiverSignatureUrl, DateTimeOffset? CancelledAt, string? CancellationReason,
        IReadOnlyList<string> AvailableActions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CompletionEffects? Effects { get; init; }
    }
    public sealed record CompletionEffects(string TransportStatus, string? RoutePlanStatus, string? TransportPlanStatus,
        Guid? ClosedRoutePlanRouteId, Guid? ActivatedRoutePlanRouteId,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ClaimWindowEndsAt);
}
