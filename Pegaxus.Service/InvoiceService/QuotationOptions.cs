namespace Pegaxus.Service.InvoiceService;

/// <summary>Unit prices (whole VND) and deposit rate for generated quotations; defaults follow business/quotation.md.</summary>
public sealed class QuotationOptions
{
    public long BaseFareTierS { get; set; } = 1_600;
    public long BaseFareTierM { get; set; } = 2_400;
    public long BaseFareTierL { get; set; } = 3_400;
    public long LoadSurchargePerHorse { get; set; } = 120;
    public long FeedPerHorseDay { get; set; } = 35;
    public long VeterinaryPerHorse { get; set; } = 110;
    public long HandlingPerHorse { get; set; } = 50;
    public long BiosecurityFlat { get; set; } = 150;
    public long ComplianceFilingFlat { get; set; } = 250;
    public long ClinicalCheckPerHorse { get; set; } = 90;
    public long InsurancePerHorse { get; set; } = 80;
    public int DepositRatePercent { get; set; } = 40;
    public int DefaultTransitDays { get; set; } = 2;

    public const int MinDepositRatePercent = 30;
    public const int MaxDepositRatePercent = 50;

    public bool IsValid() =>
        new[] { BaseFareTierS, BaseFareTierM, BaseFareTierL, LoadSurchargePerHorse, FeedPerHorseDay, VeterinaryPerHorse,
            HandlingPerHorse, BiosecurityFlat, ComplianceFilingFlat, ClinicalCheckPerHorse, InsurancePerHorse }.All(price => price > 0) &&
        DepositRatePercent is >= MinDepositRatePercent and <= MaxDepositRatePercent &&
        DefaultTransitDays is >= QuotationCatalog.MinTransitDays and <= QuotationCatalog.MaxTransitDays;
}
