namespace Pegaxus.Repository.Entities;

/// <summary>Header of a quotation price list. Exactly one list is active at a time; its items drive generated QUOTATION, DEPOSIT and FINAL lines.</summary>
public partial class PriceList
{
    public Guid PriceListId { get; set; }

    public string Name { get; set; } = null!;

    public string Currency { get; set; } = "VND";

    /// <summary>VAT percentage (0 to 100) applied to invoices created or saved while this list is active.</summary>
    public decimal TaxRate { get; set; }

    /// <summary>Deposit percentage (0 to 100) of the quotation subtotal.</summary>
    public decimal DepositRate { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CreatedByAccountId { get; set; }

    public Guid? UpdatedByAccountId { get; set; }

    public virtual ICollection<PriceListItem> PriceListItems { get; set; } = new List<PriceListItem>();
}
