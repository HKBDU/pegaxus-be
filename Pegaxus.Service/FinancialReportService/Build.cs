using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;
using Pegaxus.Service.InvoiceService;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.FinancialReportService;

public sealed partial class Service
{
    /// <param name="report">The stored report, or null for a provisional calculation.</param>
    private async Task<Response.FinancialReportResponse> BuildAsync(TransportInfo transport, FinancialReport? report, Totals totals, IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var dossier = await db.ComplianceDossiers.AsNoTracking().Where(item => item.TransportId == transport.TransportId && item.DossierType == DossierType.MAIN)
            .OrderBy(item => item.CreatedAt).Select(item => new { item.ComplianceDossierId, item.DossierTemplateId }).FirstOrDefaultAsync(cancellationToken);
        var suggested = await SuggestionsAsync(transport, dossier?.ComplianceDossierId, dossier?.DossierTemplateId, cancellationToken);
        return new(clock.GetUtcNow(), report?.FinancialReportId, report?.ReportCode, report is null,
            report is not null && await IsStaleAsync(transport, report, cancellationToken), warnings,
            new(transport.TransportId, transport.TransportCode, transport.Status.ToString()), new(transport.BookingId, transport.BookingCode),
            dossier?.DossierTemplateId, Vnd.Currency, Vnd.ToLong(totals.Invoiced), Vnd.ToLong(totals.Paid), Vnd.ToLong(totals.EmergencyCost),
            Vnd.ToLong(totals.Invoiced - totals.Paid), report?.SummaryNote, AsUtc(report?.GeneratedAt), await ReconcileAsync(transport.BookingId, cancellationToken),
            suggested, AsUtc(report?.CreatedAt), AsUtc(report?.UpdatedAt));
    }

    /// <summary>True when an invoice, a receipt or an emergency cost of the trip changed after the saved numbers were worked out.</summary>
    private async Task<bool> IsStaleAsync(TransportInfo transport, FinancialReport report, CancellationToken cancellationToken)
    {
        var scanned = report.GeneratedAt;
        var invoices = db.Invoices.AsNoTracking().Where(item => item.BookingId == transport.BookingId &&
            (item.Type == InvoiceType.FINAL || item.Type == InvoiceType.SUPPLEMENTARY));
        return await invoices.AnyAsync(item => item.UpdatedAt > scanned, cancellationToken) ||
            await db.Receipts.AsNoTracking().AnyAsync(receipt => receipt.UpdatedAt > scanned && invoices.Any(invoice => invoice.InvoiceId == receipt.InvoiceId), cancellationToken) ||
            await db.EmergencyCostRequests.AsNoTracking().AnyAsync(item => item.UpdatedAt > scanned && item.IncidentReport.TransportId == transport.TransportId, cancellationToken);
    }

    /// <summary>R23: money that does not agree between an invoice and its receipts. Only a warning; nothing is blocked.</summary>
    private async Task<Response.Reconciliation> ReconcileAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId && item.Status != InvoiceStatus.CANCELLED &&
                (item.Type == InvoiceType.DEPOSIT || item.Type == InvoiceType.FINAL || item.Type == InvoiceType.SUPPLEMENTARY))
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.InvoiceId).Select(item => new { item.InvoiceId, item.InvoiceCode, item.Status, item.TotalAmount }).ToListAsync(cancellationToken);
        var ids = invoices.Select(item => item.InvoiceId).ToArray();
        var confirmed = (await db.Receipts.AsNoTracking().Where(item => ids.Contains(item.InvoiceId) && item.Status == ReceiptStatus.CONFIRMED)
            .Select(item => new { item.InvoiceId, item.Amount }).ToListAsync(cancellationToken)).ToLookup(item => item.InvoiceId, item => item.Amount);
        var items = new List<Response.ReconciliationItem>();
        foreach (var invoice in invoices)
        {
            var amounts = confirmed[invoice.InvoiceId].ToArray();
            var actual = amounts.Sum();
            string? issue = amounts.Length > 1 ? "MULTIPLE_CONFIRMED_RECEIPTS"
                : amounts.Length == 0 && invoice.Status == InvoiceStatus.PAID ? "PAID_WITHOUT_RECEIPT"
                : amounts.Length == 1 && invoice.Status != InvoiceStatus.PAID ? "RECEIPT_ON_UNPAID_INVOICE"
                : amounts.Length == 1 && actual != invoice.TotalAmount ? "RECEIPT_AMOUNT_MISMATCH" : null;
            if (issue is not null) items.Add(new(invoice.InvoiceId, invoice.InvoiceCode, issue, Vnd.ToLong(invoice.TotalAmount), Vnd.ToLong(actual)));
        }
        return new(items.Count > 0, items);
    }

    /// <summary>
    /// R24: the nine standard lines at the list prices for the trip's real horse count and duration, for the manager to copy into the FINAL invoice,
    /// plus one reference line for the approved emergency costs. They are a suggestion only: the invoice itself is built from the issued quotation.
    /// </summary>
    private async Task<IReadOnlyList<Response.SuggestedItem>> SuggestionsAsync(TransportInfo transport, Guid? dossierId, Guid? templateId, CancellationToken cancellationToken)
    {
        var items = new List<Response.SuggestedItem>();
        var horseCount = await db.BookingHorses.AsNoTracking().CountAsync(item => item.BookingId == transport.BookingId, cancellationToken);
        // A booking holds 1 to 9 horses (BR-BOOKING-02), so the list prices always apply; odd legacy data just gets no standard lines instead of a failing page.
        if (QuotationCatalog.Supports(horseCount))
        {
            var prices = Prices;
            var days = transport.ActualDepartureAt is { } departure && transport.ActualArrivalAt is { } arrival && arrival > departure
                ? Math.Clamp((int)Math.Ceiling((arrival - departure).TotalHours / 24), QuotationCatalog.MinTransitDays, QuotationCatalog.MaxTransitDays)
                : prices.DefaultTransitDays;
            var dossier = dossierId is { } id ? new[] { new Response.SourceRef("ComplianceDossier", id) } : [];
            var lines = QuotationCatalog.Generate(prices, horseCount, days);
            // The catalog lists its nine lines in a fixed order; the basis of each line follows that order (its golden-table test guards it).
            var basis = new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["vehicleTier"] = "TIER_" + QuotationCatalog.Tier(horseCount), ["horseCount"] = horseCount },
                new Dictionary<string, object?> { ["horseCount"] = horseCount },
                new Dictionary<string, object?> { ["horseCount"] = horseCount, ["transportDays"] = days },
                new Dictionary<string, object?> { ["horseCount"] = horseCount },
                new Dictionary<string, object?> { ["horseCount"] = horseCount },
                new Dictionary<string, object?>(),
                new Dictionary<string, object?> { ["dossierTemplateId"] = templateId },
                new Dictionary<string, object?> { ["horseCount"] = horseCount },
                new Dictionary<string, object?> { ["horseCount"] = horseCount }
            };
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                // The two filing lines only exist when the trip has a legal dossier.
                if (line.ItemType == InvoiceItemType.COMPLIANCE_FEE && dossierId is null) continue;
                items.Add(new(line.ItemType.ToString(), line.Description, line.Quantity, line.UnitPrice, null, basis[index],
                    line.ItemType == InvoiceItemType.COMPLIANCE_FEE ? dossier : [], null));
            }
        }

        var costs = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.Status == EmergencyCostStatus.APPROVED && item.ApprovedAmount > 0 &&
                item.IncidentReport.TransportId == transport.TransportId).OrderBy(item => item.IncidentReport.IncidentCode).ThenBy(item => item.EmergencyCostRequestId)
            .Select(item => new { item.EmergencyCostRequestId, item.IncidentReport.IncidentCode, item.ApprovedAmount }).ToListAsync(cancellationToken);
        if (costs.Count > 0)
            items.Add(new(InvoiceItemType.EMERGENCY_COST.ToString(), "Emergency cost (approved)", 1, null, Vnd.ToLong(costs.Sum(item => item.ApprovedAmount!.Value)),
                new Dictionary<string, object?> { ["requestCount"] = costs.Count, ["incidentCodes"] = costs.Select(item => item.IncidentCode).Distinct().ToArray() },
                costs.Select(item => new Response.SourceRef("EmergencyCostRequest", item.EmergencyCostRequestId)).ToList(), null));
        return items;
    }
}
