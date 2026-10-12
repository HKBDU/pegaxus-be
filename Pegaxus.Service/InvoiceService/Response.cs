namespace Pegaxus.Service.InvoiceService;

public static class Response
{
    public sealed record CancelResponse(Guid InvoiceId, string InvoiceNumber, string Status, DateTimeOffset CancelledAt,
        string CancellationReason, IReadOnlyList<string> AvailableActions, DateTimeOffset UpdatedAt);
}
