using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.FinancialReportService;

public sealed partial class Service
{
    /// <summary>
    /// Keeps a saved report in step with the money of its booking: an invoice issued or cancelled, a payment confirmed, or an emergency cost
    /// decided after delivery changes the four amounts at once, so nobody has to press Refresh. A booking without a report is left alone.
    /// Call it inside the caller's transaction, after its SaveChanges, so the scan sees the change being made.
    /// </summary>
    public static Task SyncForBookingAsync(AppDbContext db, TimeProvider clock, Guid bookingId, CancellationToken cancellationToken) =>
        SyncAsync(db, clock, db.FinancialReports.Where(item => item.Transport.BookingId == bookingId), cancellationToken);

    /// <inheritdoc cref="SyncForBookingAsync"/>
    public static Task SyncForTransportAsync(AppDbContext db, TimeProvider clock, Guid? transportId, CancellationToken cancellationToken) =>
        transportId is null ? Task.CompletedTask : SyncAsync(db, clock, db.FinancialReports.Where(item => item.TransportId == transportId), cancellationToken);

    private static async Task SyncAsync(AppDbContext db, TimeProvider clock, IQueryable<FinancialReport> query, CancellationToken cancellationToken)
    {
        var reports = await query.ToListAsync(cancellationToken);
        if (reports.Count == 0) return;
        var service = new Service(db, clock);
        var now = Truncate(clock.GetUtcNow());
        foreach (var report in reports) Apply(report, await service.TotalsAsync(report.TransportId, cancellationToken), now);
        await db.SaveChangesAsync(cancellationToken);
    }
}
