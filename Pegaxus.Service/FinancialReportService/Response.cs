namespace Pegaxus.Service.FinancialReportService;

public static class Response
{
    public sealed record TransportBrief(Guid TransportId, string TransportCode, string Status);
    public sealed record BookingBrief(Guid BookingId, string BookingCode);

    public sealed record ReconciliationItem(Guid InvoiceId, string InvoiceNumber, string Issue, long ExpectedAmount, long ActualAmount);
    public sealed record Reconciliation(bool HasMismatch, IReadOnlyList<ReconciliationItem> Items);

    public sealed record SourceRef(string Type, Guid Id);

    /// <summary>One line the manager may copy into the FINAL invoice (R24). The emergency cost line has no price: the manager decides whether to charge it.</summary>
    public sealed record SuggestedItem(string ItemType, string Description, int Quantity, long? SuggestedPrice, long? ReferenceAmount,
        IReadOnlyDictionary<string, object?> Basis, IReadOnlyList<SourceRef> SourceRefs, string? Warning);

    public sealed record FinancialReportResponse(DateTimeOffset ServerTime, Guid? FinancialReportId, string? ReportCode, bool IsProvisional, bool IsStale,
        IReadOnlyList<string> Warnings, TransportBrief Transport, BookingBrief Booking, Guid? DossierTemplateId, string Currency,
        long TotalInvoicedAmount, long TotalPaidAmount, long TotalEmergencyCost, long OutstandingAmount, string? SummaryNote, DateTimeOffset? GeneratedAt,
        Reconciliation Reconciliation, IReadOnlyList<SuggestedItem> SuggestedItems, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt);
}
