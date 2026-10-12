namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record RecordPaymentResponse(ReceiptResponse Receipt, PaidInvoiceResponse Invoice, PaymentEffects Effects);
    public sealed record ReceiptResponse(Guid ReceiptId, string ReceiptCode, decimal Amount, string Currency,
        string PaymentMethod, string Status, string? TransactionReference, DateTimeOffset PaidAt,
        DateTimeOffset ConfirmedAt, string? Note, RecordedByResponse RecordedBy);
    public sealed record RecordedByResponse(Guid AccountId, string FullName);
    public sealed record PaidInvoiceResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        DateTimeOffset PaidAt, IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);
    public sealed record PaymentEffects(string? TransportPlanStatus, bool CreditNoteUnlocked);
}
