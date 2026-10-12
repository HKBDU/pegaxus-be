namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record StatusHistoryResponse(Guid HistoryId, string Type, string? PreviousStatus, string NewStatus,
        string? Reason, ChangedByResponse ChangedBy, DateTimeOffset ChangedAt);
    public sealed record ChangedByResponse(Guid? AccountId, string FullName, string? Role);
}
