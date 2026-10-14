namespace Pegaxus.Service.LocationService;

public interface ILocationService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid locationId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CreateAsync(Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default);
    Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid locationId, Request.UpdateRequest request, CancellationToken cancellationToken = default);
    Task<Response.DeleteResponse> DeleteAsync(Guid accountId, Guid locationId, CancellationToken cancellationToken = default);
}
