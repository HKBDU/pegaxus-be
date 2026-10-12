namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record IssueResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        DateOnly InvoiceDate, DateTimeOffset IssuedAt, decimal GrandTotal, string Currency, DateOnly? DueDate,
        IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);
}
