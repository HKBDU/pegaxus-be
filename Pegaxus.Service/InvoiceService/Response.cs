namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record OnlinePaymentResponse(ReceiptResponse Receipt, string PaymentUrl,
        IReadOnlyDictionary<string, string> CheckoutFields);

    public sealed record StatusHistoryResponse(Guid HistoryId, string Type, string? PreviousStatus, string NewStatus,
        string? Reason, ChangedByResponse ChangedBy, DateTimeOffset ChangedAt);
    public sealed record ChangedByResponse(Guid? AccountId, string FullName, string? Role);

    public sealed record CancelResponse(Guid InvoiceId, string InvoiceNumber, string Status, DateTimeOffset CancelledAt,
        string CancellationReason, IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);

    public sealed record RecordPaymentResponse(ReceiptResponse Receipt, PaidInvoiceResponse Invoice, PaymentEffects Effects);
    public sealed record PaidInvoiceResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        DateTimeOffset PaidAt, IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);
    public sealed record PaymentEffects(string? TransportPlanStatus, bool CreditNoteUnlocked);

    public sealed record IssueResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        DateOnly InvoiceDate, DateTimeOffset IssuedAt, decimal GrandTotal, string Currency, DateOnly? DueDate,
        IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);

    public sealed record InvoiceResponse(Guid InvoiceId, string InvoiceNumber, string Type, string Status, bool IsPaymentRequired,
        DateOnly InvoiceDate, DateOnly? DueDate, string Currency, string? Note, CustomerResponse Customer,
        IReadOnlyList<InvoiceItemResponse> Items, SummaryResponse Summary, ContactResponse ContactPerson,
        DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt, DateTimeOffset? CancelledAt, string? CancellationReason,
        BookingResponse Booking, TransportResponse? Transport, PlanResponse? TransportPlan,
        IReadOnlyList<ReceiptResponse> Receipts, IReadOnlyList<string> AvailableActions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        // shortcut: payment remains null until Flow 6 P-01/P-02 fields exist.
        public object? Payment => null;
    }

    public sealed record InvoiceItemResponse(Guid InvoiceItemId, string ItemType, string Description, decimal Quantity,
        decimal Price, decimal Total, HorseResponse? Horse, bool IsDepositDeduction, string? Note)
    {
        // shortcut: itemDate remains null until Flow 6 P-03 exists; R16 defers emergency cost links.
        public DateOnly? ItemDate => null;
        public Guid? EmergencyCostRequestId => null;
    }
    public sealed record ReceiptResponse(Guid ReceiptId, string ReceiptCode, decimal Amount, string Currency, string PaymentMethod,
        string Status, string? TransactionReference, DateTimeOffset? PaidAt, DateTimeOffset? ConfirmedAt, string? Note, RecordedByResponse? RecordedBy);
    public sealed record RecordedByResponse(Guid AccountId, string FullName);


    public sealed record ListResponse(IReadOnlyList<InvoiceListItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record InvoiceListItem(Guid InvoiceId, string InvoiceNumber, string Type, string Status,
        Guid BookingId, string BookingCode, Guid? TransportPlanId, string CustomerName, decimal TotalAmount,
        string Currency, DateOnly? DueDate, DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public sealed record SuggestionResponse(Guid BookingId, string BookingCode, string BookingStatus,
        Guid TransportId, string TransportStatus, string Type, string? Currency, bool IsPaymentRequired,
        IReadOnlyList<ItemResponse> Items);

    public sealed class DraftResponse
    {
        public Guid InvoiceId { get; init; }
        public required string InvoiceNumber { get; init; }
        public required string Type { get; init; }
        public string Status => "DRAFT";
        public bool IsPaymentRequired => Type != "QUOTATION";
        public DateOnly InvoiceDate { get; init; }
        public DateOnly DueDate { get; init; }
        public required string Currency { get; init; }
        public string? Note { get; init; }
        public object? Payment => null;
        public required CustomerResponse Customer { get; init; }
        public required IReadOnlyList<ItemResponse> Items { get; init; }
        public required SummaryResponse Summary { get; init; }
        public required ContactResponse ContactPerson { get; init; }
        public DateTimeOffset? IssuedAt => null;
        public DateTimeOffset? PaidAt => null;
        public DateTimeOffset? CancelledAt => null;
        public string? CancellationReason => null;
        public required BookingResponse Booking { get; init; }
        public TransportResponse? Transport { get; init; }
        public PlanResponse? TransportPlan { get; init; }
        public IReadOnlyList<object> Receipts => Array.Empty<object>();
        public IReadOnlyList<string> AvailableActions => ["EDIT", "ISSUE", "CANCEL", "CREATE_ANOTHER"];
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed record CustomerResponse(Guid CustomerId, string CustomerCode, string Name, string? Phone, string? Address, string Email);
    public sealed record ContactResponse(string Name, string Email, string? Phone);
    public sealed record BookingResponse(Guid BookingId, string BookingCode, string Status, string OriginAddress,
        string OriginCountry, string DestinationAddress, string DestinationCountry, DateOnly RequestedDepartureDate);
    public sealed record TransportResponse(Guid TransportId, string TransportCode, string Status);
    public sealed record PlanResponse(Guid TransportPlanId, string PlanCode, string Status);
    public sealed record HorseResponse(Guid HorseId, string HorseName);
    public sealed record ItemResponse(Guid? InvoiceItemId, string ItemType, string Description, DateOnly? ItemDate,
        decimal Quantity, decimal Price, decimal Total, HorseResponse? Horse, Guid? EmergencyCostRequestId,
        bool IsDepositDeduction, string? Note);
    public sealed record SummaryResponse(decimal Subtotal, decimal Tax, decimal DepositDeduction, decimal GrandTotal)
    {
        public decimal Discount => 0;
        public decimal TaxRatePercent => 20;
    }
}
