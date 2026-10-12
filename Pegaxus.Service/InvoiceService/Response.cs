namespace Pegaxus.Service.InvoiceService;

public static class Response
{
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
        public required TransportResponse Transport { get; init; }
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
