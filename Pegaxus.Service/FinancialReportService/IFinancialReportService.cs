namespace Pegaxus.Service.FinancialReportService;

public interface IFinancialReportService
{
    /// <summary>
    /// Creates the transport's financial report when it has none (flow6 R27, UC-66 automatic trigger). An existing report is left
    /// untouched: refreshing its numbers is a separate, deliberate action.
    /// </summary>
    Task EnsureAsync(Guid transportId, CancellationToken cancellationToken = default);

    /// <summary>FS1: the stored report of a completed transport, or a provisional calculation (never saved) of an unfinished one when allowed.</summary>
    Task<Response.FinancialReportResponse> GetAsync(Guid accountId, Guid transportId, bool allowProvisional, CancellationToken cancellationToken = default);

    /// <summary>FS2: scans the invoices, receipts and emergency costs again and overwrites the four amounts.</summary>
    Task<Response.FinancialReportResponse> RefreshAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default);

    /// <summary>FS3: saves the manager's evaluation note; the amounts are not touched.</summary>
    Task<Response.FinancialReportResponse> UpdateNoteAsync(Guid accountId, Guid transportId, Request.UpdateNoteRequest request, CancellationToken cancellationToken = default);
}
