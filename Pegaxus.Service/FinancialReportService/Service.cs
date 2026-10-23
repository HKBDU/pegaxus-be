using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.FinancialReportService;

public sealed class Service(AppDbContext db, TimeProvider clock) : IFinancialReportService
{
    public async Task EnsureAsync(Guid transportId, CancellationToken cancellationToken = default)
    {
        if (await db.FinancialReports.AsNoTracking().AnyAsync(item => item.TransportId == transportId, cancellationToken)) return;
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            if (await db.FinancialReports.AnyAsync(item => item.TransportId == transportId, cancellationToken)) return;
            var totals = await TotalsAsync(transportId, cancellationToken);
            var now = Truncate(clock.GetUtcNow());
            db.FinancialReports.Add(new FinancialReport
            {
                FinancialReportId = Guid.NewGuid(), TransportId = transportId, Currency = Vnd.Currency,
                ReportCode = await CodeGeneration.NextAsync("FR", now, code => db.FinancialReports.AnyAsync(item => item.ReportCode == code, cancellationToken),
                    "CONCURRENT_UPDATE", "Unable to generate a unique report code. Please try again."),
                TotalInvoicedAmount = totals.Invoiced, TotalPaidAmount = totals.Paid, TotalEmergencyCost = totals.EmergencyCost,
                OutstandingAmount = totals.Invoiced - totals.Paid, GeneratedAt = now, CreatedAt = now, UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation })
        {
            // Someone else created the report at the same moment; the goal (a report exists) is met.
            db.ChangeTracker.Clear();
        }
    }

    internal sealed record Totals(decimal Invoiced, decimal Paid, decimal EmergencyCost);

    /// <summary>R23: income is the FINAL and SUPPLEMENTARY invoices that are not cancelled, the paid part is their confirmed receipts, the cost is the approved emergency costs.</summary>
    internal async Task<Totals> TotalsAsync(Guid transportId, CancellationToken cancellationToken)
    {
        var bookingId = await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId).Select(item => item.BookingId).SingleAsync(cancellationToken);
        var invoices = db.Invoices.AsNoTracking().Where(item => item.BookingId == bookingId && item.Status != InvoiceStatus.CANCELLED &&
            (item.Type == InvoiceType.FINAL || item.Type == InvoiceType.SUPPLEMENTARY));
        var invoiced = await invoices.SumAsync(item => (decimal?)item.TotalAmount, cancellationToken) ?? 0;
        var paid = await db.Receipts.AsNoTracking().Where(receipt => receipt.Status == ReceiptStatus.CONFIRMED &&
            invoices.Any(invoice => invoice.InvoiceId == receipt.InvoiceId)).SumAsync(receipt => (decimal?)receipt.Amount, cancellationToken) ?? 0;
        var emergency = await db.EmergencyCostRequests.AsNoTracking().Where(item => item.Status == EmergencyCostStatus.APPROVED &&
            item.IncidentReport.TransportId == transportId).SumAsync(item => item.ApprovedAmount, cancellationToken) ?? 0;
        return new(invoiced, paid, emergency);
    }
}
