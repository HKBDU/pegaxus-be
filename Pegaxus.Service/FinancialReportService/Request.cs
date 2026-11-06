namespace Pegaxus.Service.FinancialReportService;

public static class Request
{
    public sealed class UpdateNoteRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? SummaryNote { get; set; }
    }
}
