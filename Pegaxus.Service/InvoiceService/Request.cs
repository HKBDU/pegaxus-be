using System.Text.Json;

namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class CancelRequest
    {
        public string CancellationReason { get; set; } = "";
    }

    public sealed class RecordPaymentRequest
    {
        public decimal? Amount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string? TransactionReference { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public string? Note { get; set; }
    }

    public sealed class IssueRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }

    public sealed class BookingListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }


    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Type { get; set; }
        public string? Status { get; set; }
        public Guid? BookingId { get; set; }
        public Guid? TransportPlanId { get; set; }
        public DateOnly? IssuedFrom { get; set; }
        public DateOnly? IssuedTo { get; set; }
        public DateOnly? DueFrom { get; set; }
        public DateOnly? DueTo { get; set; }
        public string? Search { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }

    public sealed class SuggestionRequest
    {
        public string Type { get; set; } = string.Empty;
        public int? TransitDays { get; set; }
    }

    public sealed class CreateRequest
    {
        public string Type { get; set; } = string.Empty;
        public DateOnly? DueDate { get; set; }
        public string? Note { get; set; }
        public int? TransitDays { get; set; }
        public JsonElement? Payment { get; set; }
        public List<ItemRequest>? Items { get; set; }
    }

    public sealed class UpdateRequest
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? Type { get; set; }
        public DateOnly? DueDate { get; set; }
        public string? Note { get; set; }
        public int? TransitDays { get; set; }
        public JsonElement? Payment { get; set; }
        public List<ItemRequest>? Items { get; set; }
    }

    public sealed class ItemRequest
    {
        public Guid? InvoiceItemId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public JsonElement? ItemDate { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public Guid? HorseId { get; set; }
        public string? Note { get; set; }
        public bool IsDepositDeduction { get; set; }
    }
}
