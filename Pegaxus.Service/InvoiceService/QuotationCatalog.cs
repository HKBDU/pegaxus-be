using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

/// <summary>Pure quotation formula from business/quotation.md; the unit prices come from the active price list, no database or clock access here.</summary>
public static class QuotationCatalog
{
    public const int MaxHorses = 9;
    public const int MinTransitDays = 1;
    public const int MaxTransitDays = 60;
    public const int DefaultTransitDays = 2;

    /// <summary>TRIP is billed once, HORSE once per horse, HORSE_DAY once per horse and transit day.</summary>
    public static readonly string[] Units = ["TRIP", "HORSE", "HORSE_DAY"];

    public sealed record Line(InvoiceItemType ItemType, string Description, int Quantity, long UnitPrice,
        Guid? PriceListItemId = null, string ItemCode = "", string Unit = "TRIP", string? VehicleTier = null);

    public static bool Supports(int horseCount) => horseCount is >= 1 and <= MaxHorses;

    public static string Tier(int horseCount) => horseCount switch
    {
        <= 3 => "S",
        <= 6 => "M",
        _ => "L"
    };

    public static IReadOnlyList<Line> Generate(PriceListSnapshot prices, int horseCount, int transitDays)
    {
        if (!Supports(horseCount))
            throw new UnprocessableEntityException("The formula covers 1 to 9 horses. Enter the quotation lines manually", "HORSE_COUNT_UNSUPPORTED");
        var tier = Tier(horseCount);
        return prices.Items.Where(item => item.VehicleTier is null || item.VehicleTier == tier)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.ItemCode, StringComparer.Ordinal)
            .Select(item => new Line(item.ItemType, item.ItemName, Quantity(item.Unit, horseCount, transitDays), item.UnitPrice,
                item.PriceListItemId, item.ItemCode, item.Unit, item.VehicleTier)).ToList();
    }

    private static int Quantity(string unit, int horseCount, int transitDays) => unit switch
    {
        "TRIP" => 1,
        "HORSE" => horseCount,
        "HORSE_DAY" => horseCount * transitDays,
        _ => throw new UnprocessableEntityException($"The price list unit {unit} is not supported", "PRICE_LIST_UNIT_UNSUPPORTED")
    };

    // Rate applies to the pre-tax subtotal so the deposit invoice adds VAT exactly once.
    public static long Deposit(long quotationSubtotal, decimal ratePercent) =>
        (long)decimal.Round(quotationSubtotal * ratePercent / 100m, 0, MidpointRounding.AwayFromZero);
}
