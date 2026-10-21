namespace Pegaxus.Service.HandoverService;

public static class Request
{
    public sealed class CreateRequest
    {
        public string HandoverType { get; set; } = string.Empty;
        public Guid? FromRoutePlanRouteId { get; set; }
        public Guid? ToRoutePlanRouteId { get; set; }
        public bool StartNow { get; set; } = true;
        public DateTimeOffset? PlannedHandoverAt { get; set; }
        public string? LocationName { get; set; }
        public string? Address { get; set; }
        public string? Country { get; set; }
        public string? FromPartyName { get; set; }
        public string? FromContactName { get; set; }
        public string? FromContactPhone { get; set; }
        public string? ToPartyName { get; set; }
        public string? ToContactName { get; set; }
        public string? ToContactPhone { get; set; }
        public string? FromType { get; set; }
        public Guid? FromCustomerId { get; set; }
        public Guid? FromEmployeeId { get; set; }
        public string? ToType { get; set; }
        public Guid? ToCustomerId { get; set; }
        public Guid? ToEmployeeId { get; set; }
    }

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
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
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
        public UploadService.Request.UploadedAssetReference? SenderSignature { get; set; }
        public UploadService.Request.UploadedAssetReference? ReceiverSignature { get; set; }
    }
}
