namespace Pegaxus.Service.TransportProviderService;

public interface IService
{
    Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default);
    Task<Response.WriteResponse> UpdateAsync(Guid accountId, Guid id, Request.UpdateRequest request, CancellationToken cancellationToken = default);
    Task<Response.StatusResponse> UpdateStatusAsync(Guid accountId, Guid id, Request.StatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid accountId, Guid id, CancellationToken cancellationToken = default);
}
