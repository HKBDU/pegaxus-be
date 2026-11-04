using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

/// <summary>One standard fee line of the active price list, with the unit price in whole VND.</summary>
public sealed record PriceItem(Guid? PriceListItemId, string ItemCode, string ItemName, InvoiceItemType ItemType, string Unit,
    long UnitPrice, string? VehicleTier, int SortOrder);

/// <summary>The active price list as the quotation formula sees it. <see cref="PriceListId"/> is null for the built-in defaults.</summary>
public sealed record PriceListSnapshot(Guid? PriceListId, decimal TaxRate, decimal DepositRate, IReadOnlyList<PriceItem> Items)
{
    /// <summary>The list prices of business/quotation.md; used only while the database holds no price list at all.</summary>
    public static PriceListSnapshot Default { get; } = new(null, 20m, 40m,
    [
        new(null, "TRUCK_S", "Base vehicle fare, Tier S", InvoiceItemType.TRANSPORT_FEE, "TRIP", 1_600, "S", 10),
        new(null, "TRUCK_M", "Base vehicle fare, Tier M", InvoiceItemType.TRANSPORT_FEE, "TRIP", 2_400, "M", 10),
        new(null, "TRUCK_L", "Base vehicle fare, Tier L", InvoiceItemType.TRANSPORT_FEE, "TRIP", 3_400, "L", 10),
        new(null, "HORSE_LOAD", "Load and compartment surcharge", InvoiceItemType.TRANSPORT_FEE, "HORSE", 120, null, 20),
        new(null, "FEEDING", "Feed and electrolyte water", InvoiceItemType.HORSE_SERVICE_FEE, "HORSE_DAY", 35, null, 30),
        new(null, "MEDICAL", "Veterinary care and sterile bedding", InvoiceItemType.HORSE_SERVICE_FEE, "HORSE", 110, null, 40),
        new(null, "HANDLING", "Hydraulic lift handling and e-POD", InvoiceItemType.HANDLING_FEE, "HORSE", 50, null, 50),
        new(null, "DISINFECTION", "Vehicle biosecurity disinfection", InvoiceItemType.HANDLING_FEE, "TRIP", 150, null, 60),
        new(null, "COMPLIANCE_FILING", "TRACES-NT and customs filing", InvoiceItemType.COMPLIANCE_FEE, "TRIP", 250, null, 70),
        new(null, "CLINICAL_CHECK", "Clinical exam and microchip check at BCP", InvoiceItemType.COMPLIANCE_FEE, "HORSE", 90, null, 80),
        new(null, "INSURANCE", "Equine transit liability insurance", InvoiceItemType.OTHER, "HORSE", 80, null, 90)
    ]);
}

internal static class PriceListLoader
{
    /// <summary>
    /// The active price list. With no price list in the database at all the built-in defaults apply; with lists but none active
    /// nothing can be quoted, so the caller gets 422 NO_ACTIVE_PRICE_LIST. Lines are only read when <paramref name="withItems"/> is set.
    /// </summary>
    public static async Task<PriceListSnapshot> LoadActivePriceListAsync(this AppDbContext db, CancellationToken cancellationToken, bool withItems = true)
    {
        var list = await db.PriceLists.AsNoTracking().Where(item => item.IsActive)
            .Select(item => new { item.PriceListId, item.TaxRate, item.DepositRate }).SingleOrDefaultAsync(cancellationToken);
        if (list is null)
        {
            if (await db.PriceLists.AsNoTracking().AnyAsync(cancellationToken))
                throw new UnprocessableEntityException("There is no active price list. Activate one before creating invoices", "NO_ACTIVE_PRICE_LIST");
            return PriceListSnapshot.Default;
        }
        if (!withItems) return new(list.PriceListId, list.TaxRate, list.DepositRate, []);
        var items = await db.PriceListItems.AsNoTracking().Where(item => item.PriceListId == list.PriceListId).ToListAsync(cancellationToken);
        return new(list.PriceListId, list.TaxRate, list.DepositRate, items.Select(item => new PriceItem(item.PriceListItemId, item.ItemCode,
            item.ItemName, item.ItemType, item.Unit, Vnd.ToLong(item.UnitPrice), item.VehicleTier, item.SortOrder)).ToList());
    }
}
