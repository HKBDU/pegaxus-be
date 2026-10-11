namespace Pegaxus.Service.VehicleService;

public interface IService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.VehicleResponse> GetAsync(Guid vehicleId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.VehicleResponse> CreateAsync(Request.CreateRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.UpdateResponse> UpdateAsync(Guid vehicleId, Request.UpdateRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.StatusResponse> ChangeStatusAsync(Guid vehicleId, Request.StatusRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
