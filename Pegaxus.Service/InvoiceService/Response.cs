namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record SuggestionResponse(Guid BookingId, string BookingCode, string BookingStatus,
        Guid TransportId, string TransportStatus, string Type, string? Currency, bool IsPaymentRequired,
        IReadOnlyList<ItemResponse> Items);

    public sealed record ItemResponse(Guid? InvoiceItemId, string ItemType, string Description,
        DateOnly? ItemDate, decimal Quantity, decimal Price, decimal Total, object? Horse,
        Guid? EmergencyCostRequestId, bool IsDepositDeduction, string? Note);
}
