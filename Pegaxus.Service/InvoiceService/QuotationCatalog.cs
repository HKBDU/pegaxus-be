using Pegaxus.Repository.Enum;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.InvoiceService;

/// <summary>Pure quotation formula from business/quotation.md; no database or clock access.</summary>
public static class QuotationCatalog
{
    public const int MaxHorses = 9;
    public const int MinTransitDays = 1;
    public const int MaxTransitDays = 60;

    public sealed record Line(InvoiceItemType ItemType, string Description, int Quantity, long UnitPrice);

    public static bool Supports(int horseCount) => horseCount is >= 1 and <= MaxHorses;

    public static string Tier(int horseCount) => horseCount switch
    {
        <= 3 => "S",
        <= 6 => "M",
        _ => "L"
    };

    public static IReadOnlyList<Line> Generate(QuotationOptions prices, int horseCount, int transitDays)
    {
        if (!Supports(horseCount))
            throw new UnprocessableEntityException("The formula covers 1 to 9 horses. Enter the quotation lines manually", "HORSE_COUNT_UNSUPPORTED");
        var baseFare = Tier(horseCount) switch { "S" => prices.BaseFareTierS, "M" => prices.BaseFareTierM, _ => prices.BaseFareTierL };
        return
        [
            new(InvoiceItemType.TRANSPORT_FEE, $"Base vehicle fare, Tier {Tier(horseCount)}", 1, baseFare),
            new(InvoiceItemType.TRANSPORT_FEE, "Load and compartment surcharge", horseCount, prices.LoadSurchargePerHorse),
            new(InvoiceItemType.HORSE_SERVICE_FEE, "Feed and electrolyte water", horseCount * transitDays, prices.FeedPerHorseDay),
            new(InvoiceItemType.HORSE_SERVICE_FEE, "Veterinary care and sterile bedding", horseCount, prices.VeterinaryPerHorse),
            new(InvoiceItemType.HANDLING_FEE, "Hydraulic lift handling and e-POD", horseCount, prices.HandlingPerHorse),
            new(InvoiceItemType.HANDLING_FEE, "Vehicle biosecurity disinfection", 1, prices.BiosecurityFlat),
            new(InvoiceItemType.COMPLIANCE_FEE, "TRACES-NT and customs filing", 1, prices.ComplianceFilingFlat),
            new(InvoiceItemType.COMPLIANCE_FEE, "Clinical exam and microchip check at BCP", horseCount, prices.ClinicalCheckPerHorse),
            new(InvoiceItemType.OTHER, "Equine transit liability insurance", horseCount, prices.InsurancePerHorse)
        ];
    }

    // Rate applies to the pre-tax subtotal so the deposit invoice adds VAT exactly once.
    public static long Deposit(long quotationSubtotal, int ratePercent) =>
        (long)decimal.Round(quotationSubtotal * ratePercent / 100m, 0, MidpointRounding.AwayFromZero);
}
