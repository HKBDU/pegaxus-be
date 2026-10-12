using System.Text.Json;

namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class CreateRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public DateOnly? DueDate { get; set; }
        public string? Note { get; set; }
        public JsonElement? Payment { get; set; }
        public List<ItemRequest>? Items { get; set; }
    }

    public sealed class ItemRequest
    {
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
