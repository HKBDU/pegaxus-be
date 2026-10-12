namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record OnlinePaymentResponse(ReceiptResponse Receipt, string PaymentUrl,
        IReadOnlyDictionary<string, string> CheckoutFields);
    public sealed record ReceiptResponse(Guid ReceiptId, string ReceiptCode, decimal Amount, string Currency,
        string PaymentMethod, string Status, string? TransactionReference, DateTimeOffset? PaidAt,
        DateTimeOffset? ConfirmedAt, string? Note, object? RecordedBy);
}
