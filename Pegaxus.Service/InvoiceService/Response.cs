namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record InvoiceResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status, bool IsPaymentRequired,
        DateOnly InvoiceDate, DateOnly? DueDate, string Currency, string? Note, CustomerResponse Customer,
        IReadOnlyList<InvoiceItemResponse> Items, SummaryResponse Summary, ContactPersonResponse ContactPerson,
        DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt, DateTimeOffset? CancelledAt, string? CancellationReason,
        BookingResponse Booking, TransportResponse? Transport, TransportPlanResponse? TransportPlan,
        IReadOnlyList<ReceiptResponse> Receipts, IReadOnlyList<string> AvailableActions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        // shortcut: payment remains null until Flow 6 P-01/P-02 fields exist.
        public object? Payment => null;
    }

    public sealed record CustomerResponse(Guid CustomerId, string CustomerCode, string Name, string? Phone, string? Address, string Email);
    public sealed record ContactPersonResponse(string Name, string Email, string? Phone);
    public sealed record InvoiceItemResponse(Guid InvoiceItemId, string ItemType, string Description, decimal Quantity,
        decimal Price, decimal Total, HorseResponse? Horse, bool IsDepositDeduction, string? Note)
    {
        // shortcut: itemDate remains null until Flow 6 P-03 exists; R16 defers emergency cost links.
        public DateOnly? ItemDate => null;
        public Guid? EmergencyCostRequestId => null;
    }
    public sealed record HorseResponse(Guid HorseId, string HorseName);
    public sealed record SummaryResponse(decimal Subtotal, decimal Discount, decimal Tax, decimal TaxRatePercent, decimal DepositDeduction, decimal GrandTotal);
    public sealed record BookingResponse(Guid BookingId, string BookingCode, string Status, string OriginAddress, string OriginCountry,
        string DestinationAddress, string DestinationCountry, DateOnly RequestedDepartureDate);
    public sealed record TransportResponse(Guid TransportId, string TransportCode, string Status);
    public sealed record TransportPlanResponse(Guid TransportPlanId, string PlanCode, string Status);
    public sealed record ReceiptResponse(Guid ReceiptId, string ReceiptCode, decimal Amount, string Currency, string PaymentMethod,
        string Status, string? TransactionReference, DateTimeOffset? PaidAt, DateTimeOffset? ConfirmedAt, string? Note, RecordedByResponse? RecordedBy);
    public sealed record RecordedByResponse(Guid AccountId, string FullName);
}
