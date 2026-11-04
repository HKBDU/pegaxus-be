namespace Pegaxus.Service.PriceListService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public bool? IsActive { get; set; }
        public string? Search { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }

    public sealed class CreateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "VND";
        public decimal? TaxRate { get; set; }
        public decimal? DepositRate { get; set; }
    }

    public sealed class UpdateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Currency { get; set; } = "VND";
        public decimal? TaxRate { get; set; }
        public decimal? DepositRate { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }

    public sealed class CreateItemRequest
    {
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal? UnitPrice { get; set; }
        public string? VehicleTier { get; set; }
        public int? SortOrder { get; set; }
    }

    public sealed class UpdateItemRequest
    {
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal? UnitPrice { get; set; }
        public string? VehicleTier { get; set; }
        public int? SortOrder { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
