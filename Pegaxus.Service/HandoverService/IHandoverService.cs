namespace Pegaxus.Service.HandoverService;

public interface IHandoverService
{
    Task<Response.DetailResponse> CreateAsync(Guid transportId, Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.ListResponse> GetListAsync(Guid transportId, Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> StartAsync(Guid handoverRecordId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CancelAsync(Guid handoverRecordId, Request.CancelRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CompleteAsync(Guid handoverRecordId, Request.CompleteRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
