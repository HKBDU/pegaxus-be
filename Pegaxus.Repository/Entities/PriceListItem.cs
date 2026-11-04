using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>One standard fee line of a <see cref="PriceList"/>.</summary>
public partial class PriceListItem
{
    public Guid PriceListItemId { get; set; }

    public Guid PriceListId { get; set; }

    /// <summary>Unique within the price list; upper case letters, digits and underscore.</summary>
    public string ItemCode { get; set; } = null!;

    public string ItemName { get; set; } = null!;

    public InvoiceItemType ItemType { get; set; }

    /// <summary>TRIP (quantity 1), HORSE (quantity = horses) or HORSE_DAY (quantity = horses x transit days).</summary>
    public string Unit { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    /// <summary>S, M or L when the line only applies to that vehicle tier; null for every tier.</summary>
    public string? VehicleTier { get; set; }

    /// <summary>Order of the generated invoice lines.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual PriceList PriceList { get; set; } = null!;
}
