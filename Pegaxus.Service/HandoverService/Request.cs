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
}
