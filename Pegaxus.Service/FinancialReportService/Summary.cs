using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Repository.Extensions;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;
using Pegaxus.Service.FinancialReportService.Validator;

using static Pegaxus.Service.Common.DbTime;

namespace Pegaxus.Service.FinancialReportService;

public sealed partial class Service
{
    private sealed record TransportInfo(Guid TransportId, string TransportCode, TransportStatus Status, Guid BookingId, string BookingCode,
        DateTime? ActualDepartureAt, DateTime? ActualArrivalAt);

    public async Task<Response.FinancialReportResponse> GetAsync(Guid accountId, Guid transportId, bool allowProvisional, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        var transport = await TransportAsync(transportId, cancellationToken);
        RejectCancelled(transport);
        if (transport.Status != TransportStatus.COMPLETED)
        {
            if (!allowProvisional)
                throw new ConflictException("The transport has not been completed, so costs may be incomplete. Do you want to view a provisional summary?", "TRANSPORT_NOT_COMPLETED");
            // Numbers worked out now and shown as such; nothing is saved (UC-66 EXC-01).
            return await BuildAsync(transport, null, await TotalsAsync(transportId, cancellationToken), ["TRANSPORT_NOT_COMPLETED"], cancellationToken);
        }
        await EnsureAsync(transportId, cancellationToken);
        var report = await db.FinancialReports.AsNoTracking().SingleAsync(item => item.TransportId == transportId, cancellationToken);
        return await BuildAsync(transport, report, new(report.TotalInvoicedAmount, report.TotalPaidAmount, report.TotalEmergencyCost), [], cancellationToken);
    }

    public async Task<Response.FinancialReportResponse> RefreshAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        var transport = await TransportAsync(transportId, cancellationToken);
        RejectCancelled(transport);
        if (transport.Status != TransportStatus.COMPLETED)
            throw new ConflictException("The transport has not been completed, so costs may be incomplete.", "TRANSPORT_NOT_COMPLETED");
        FinancialReport report;
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.FinancialReports.Where(item => item.TransportId == transportId);
            await db.LockRowsAsync(query, item => item.FinancialReportId, cancellationToken);
            var existing = await query.SingleOrDefaultAsync(cancellationToken);
            if (existing is null) report = await CreateAsync(transportId, cancellationToken);
            else
            {
                if (db.Database.IsRelational()) await db.Entry(existing).ReloadAsync(cancellationToken);
                Apply(existing, await TotalsAsync(transportId, cancellationToken), Truncate(clock.GetUtcNow()));
                await db.SaveChangesAsync(cancellationToken);
                report = existing;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw Modified();
        }
        db.ChangeTracker.Clear();
        return await BuildAsync(transport, report, new(report.TotalInvoicedAmount, report.TotalPaidAmount, report.TotalEmergencyCost), [], cancellationToken);
    }

    public async Task<Response.FinancialReportResponse> UpdateNoteAsync(Guid accountId, Guid transportId, Request.UpdateNoteRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureManagerAsync(accountId, cancellationToken);
        Validation.ValidateFields(request, new UpdateNoteRequestValidator());
        var transport = await TransportAsync(transportId, cancellationToken);
        FinancialReport report;
        try
        {
            await using var transaction = await db.Database.BeginSerializableAsync(cancellationToken);
            var query = db.FinancialReports.Where(item => item.TransportId == transportId);
            await db.LockRowsAsync(query, item => item.FinancialReportId, cancellationToken);
            report = await query.SingleOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Financial report not found. Open the financial summary first.", "FINANCIAL_REPORT_NOT_FOUND");
            if (db.Database.IsRelational()) await db.Entry(report).ReloadAsync(cancellationToken);
            if (Truncate(report.UpdatedAt) != Truncate(request.LastUpdatedAt!.Value)) throw Modified();
            report.SummaryNote = Text.EmptyToNull(request.SummaryNote)?.Trim();
            var now = Truncate(clock.GetUtcNow());
            report.UpdatedAt = report.UpdatedAt >= now ? Truncate(report.UpdatedAt).AddTicks(10) : now;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw Modified();
        }
        db.ChangeTracker.Clear();
        return await BuildAsync(transport, report, new(report.TotalInvoicedAmount, report.TotalPaidAmount, report.TotalEmergencyCost), [], cancellationToken);
    }

    /// <summary>Financial figures are for Logistics Managers only (O-F6-14).</summary>
    private async Task EnsureManagerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var allowed = await db.Accounts.AsNoTracking().AnyAsync(account => account.AccountId == accountId && !account.IsDeleted && account.Status == AccountStatus.ACTIVE &&
            account.AccountType == AccountType.EMPLOYEE && account.EmployeeAccount != null && !account.EmployeeAccount.IsDeleted && account.EmployeeAccount.IsActive &&
            account.EmployeeAccount.Role == EmployeeRole.LOGISTICS_MANAGER, cancellationToken);
        if (!allowed) throw new ForbiddenException("Only Logistics Managers are authorized to view financial reports.");
    }

    private async Task<TransportInfo> TransportAsync(Guid transportId, CancellationToken cancellationToken) =>
        await db.Transports.AsNoTracking().Where(item => item.TransportId == transportId)
            .Select(item => new TransportInfo(item.TransportId, item.TransportCode, item.Status, item.BookingId, item.Booking.BookingCode,
                item.ActualDepartureAt, item.ActualArrivalAt)).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Transport not found", "TRANSPORT_NOT_FOUND");

    private static void RejectCancelled(TransportInfo transport)
    {
        if (transport.Status == TransportStatus.CANCELLED)
            throw new ConflictException("A cancelled transport has no financial summary", "INVALID_TRANSPORT_STATUS");
    }
}
