using System.Text.Json.Serialization;

namespace Pegaxus.Service.PaymentService;

public static class Request
{
    public sealed class WebhookRequest
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("notification_type")]
        public string? NotificationType { get; set; }

        [JsonPropertyName("order")]
        public Order? Order { get; set; }

        [JsonPropertyName("transaction")]
        public Transaction? Transaction { get; set; }
    }

    public sealed class Order
    {
        [JsonPropertyName("order_invoice_number")]
        public string? ReceiptCode { get; set; }

        [JsonPropertyName("order_status")]
        public string? Status { get; set; }

        [JsonPropertyName("order_amount"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? Amount { get; set; }

        [JsonPropertyName("order_currency")]
        public string? Currency { get; set; }
    }

    public sealed class Transaction
    {
        [JsonPropertyName("transaction_id")]
        public string? Reference { get; set; }

        [JsonPropertyName("transaction_type")]
        public string? Type { get; set; }

        [JsonPropertyName("transaction_status")]
        public string? Status { get; set; }

        [JsonPropertyName("payment_method")]
        public string? PaymentMethod { get; set; }

        [JsonPropertyName("transaction_amount"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? Amount { get; set; }

        [JsonPropertyName("transaction_currency")]
        public string? Currency { get; set; }
    }
}
