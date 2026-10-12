namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class RecordPaymentRequest
    {
        public decimal? Amount { get; set; }
        public string Currency { get; set; } = "";
        public string PaymentMethod { get; set; } = "";
        public string? TransactionReference { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public string? Note { get; set; }
    }
}
