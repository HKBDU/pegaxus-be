namespace Pegaxus.Service.HorseHealthService;

public interface IHorseHealthService
{
    Task<Response.OverviewResponse> GetOverviewAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.ListResponse> GetLogsAsync(Guid transportId, Guid horseId, Request.ListRequest request, Guid accountId,
        CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid horseHealthLogId, Guid accountId, CancellationToken cancellationToken = default);
}
