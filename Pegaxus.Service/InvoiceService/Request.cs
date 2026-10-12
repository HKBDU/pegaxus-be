namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class IssueRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
