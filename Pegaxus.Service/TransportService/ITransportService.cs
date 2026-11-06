namespace Pegaxus.Service.TransportService;

public interface ITransportService
{
    Task<Response.ListResponse> GetOngoingAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.AssignedResponse> GetAssignedAsync(Guid accountId, Request.AssignedRequest request, CancellationToken cancellationToken = default);
    Task<Response.OverviewResponse> GetOverviewAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default);
    Task<Response.SegmentsResponse> GetSegmentsAsync(Guid accountId, Guid transportId, CancellationToken cancellationToken = default);
}
