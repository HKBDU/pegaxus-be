namespace Pegaxus.Service.RouteService;

public interface IService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid routeId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default);
    Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid routeId, Request.UpdateRequest request, CancellationToken cancellationToken = default);
    Task<Response.DeleteResponse> DeleteAsync(Guid accountId, Guid routeId, CancellationToken cancellationToken = default);
}
