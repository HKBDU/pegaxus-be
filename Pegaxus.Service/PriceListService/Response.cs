namespace Pegaxus.Service.PriceListService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<SummaryResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class SummaryResponse
    {
        public Guid PriceListId { get; init; }
        public required string Name { get; init; }
        public required string Currency { get; init; }
        public decimal TaxRate { get; init; }
        public decimal DepositRate { get; init; }
        public bool IsActive { get; init; }
        public int ItemCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class DetailResponse
    {
        public Guid PriceListId { get; init; }
        public required string Name { get; init; }
        public required string Currency { get; init; }
        public decimal TaxRate { get; init; }
        public decimal DepositRate { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public Guid? CreatedByAccountId { get; init; }
        public Guid? UpdatedByAccountId { get; init; }
        public required IReadOnlyList<ItemResponse> Items { get; init; }
    }

    public sealed class ItemResponse
    {
        public Guid PriceListItemId { get; init; }
        public Guid PriceListId { get; init; }
        public required string ItemCode { get; init; }
        public required string ItemName { get; init; }
        public required string ItemType { get; init; }
        public required string Unit { get; init; }
        public long UnitPrice { get; init; }
        public string? VehicleTier { get; init; }
        public int SortOrder { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
