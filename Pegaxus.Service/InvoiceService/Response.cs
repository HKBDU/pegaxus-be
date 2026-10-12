namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record ListResponse(IReadOnlyList<InvoiceListItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record InvoiceListItem(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        Guid BookingId, string BookingCode, Guid? TransportPlanId, string CustomerName, decimal TotalAmount,
        string Currency, DateOnly? DueDate, DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
