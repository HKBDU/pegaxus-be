namespace Pegaxus.Service.FinancialReportService;

public interface IFinancialReportService
{
    /// <summary>
    /// Creates the transport's financial report when it has none (flow6 R27, UC-66 automatic trigger). An existing report is left
    /// untouched: refreshing its numbers is a separate, deliberate action.
    /// </summary>
    Task EnsureAsync(Guid transportId, CancellationToken cancellationToken = default);
}
