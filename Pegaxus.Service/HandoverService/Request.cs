using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.HandoverService;

public static class Request
{
    public sealed class ListRequest
    {
        public string? HandoverType { get; set; }
        public string? Status { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "sequenceNo:asc";
    }

    public sealed class CancelRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? CancellationReason { get; set; }
    }

    public sealed class CompleteRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? HorseConditionNote { get; set; }
        public string? DocumentHandoverNote { get; set; }
        public string? EquipmentHandoverNote { get; set; }
        public string? DiscrepancyNote { get; set; }
        public string? LocationName { get; set; }
        public string? Address { get; set; }
        public string? Country { get; set; }
        public string? FromPartyName { get; set; }
        public string? FromContactName { get; set; }
        public string? FromContactPhone { get; set; }
        public string? ToPartyName { get; set; }
        public string? ToContactName { get; set; }
        public string? ToContactPhone { get; set; }
        public IFormFile? SenderSignature { get; set; }
        public IFormFile? ReceiverSignature { get; set; }
    }
}
