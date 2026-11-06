namespace Pegaxus.Service.ReportService;

public interface IReportService
{
    Task<Response.OnTimeReport> GetOnTimeAsync(Guid accountId, Request.OnTimeRequest request, CancellationToken cancellationToken = default);

    Task<Response.IncidentClaimReport> GetIncidentClaimAsync(Guid accountId, Request.IncidentClaimRequest request, CancellationToken cancellationToken = default);

    Task<Response.TransitTimeReport> GetTransitTimeAsync(Guid accountId, Request.TransitTimeRequest request, CancellationToken cancellationToken = default);

    Task<Response.ExportFile> ExportAsync(Guid accountId, string? reportType, Request.ExportRequest request, CancellationToken cancellationToken = default);
}
