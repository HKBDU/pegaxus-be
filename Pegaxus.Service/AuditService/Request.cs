namespace Pegaxus.Service.AuditService;

public static class Request
{
    public sealed class ArchiveRequest
    {
        public string? Search { get; set; }
        public DateOnly? CompletedFrom { get; set; }
        public DateOnly? CompletedTo { get; set; }
        public string? OriginCountry { get; set; }
        public string? DestinationCountry { get; set; }
        public bool? HasClaim { get; set; }
        public bool IncludeCancelled { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "completedAt:desc";
    }

    public sealed class AuditRequest
    {
        public string? Blocks { get; set; }
        public int Limit { get; set; } = 200;
    }

    public sealed class ExportRequest
    {
        public string? Format { get; set; }
        public string? Mode { get; set; }
    }
}
