namespace Pegaxus.Service.AuditService;

public interface IAuditService
{
    Task<Response.ArchiveResponse> GetArchiveAsync(Guid accountId, Request.ArchiveRequest request, CancellationToken cancellationToken = default);

    Task<Response.AuditResponse> GetAuditAsync(Guid accountId, Guid transportId, Request.AuditRequest request, CancellationToken cancellationToken = default);

    Task<Response.ExportQueued> RequestExportAsync(Guid accountId, Guid transportId, Request.ExportRequest request, CancellationToken cancellationToken = default);

    Task<Response.ExportStatus> GetExportAsync(Guid accountId, Guid exportId, CancellationToken cancellationToken = default);

    /// <summary>Builds the package of a queued export job. Called by the background worker, never by a request.</summary>
    Task ProcessExportAsync(Guid exportId, CancellationToken cancellationToken = default);
}
