namespace Pegaxus.Service.TransportPlanService;

public interface IService
{
    Task<Response.CreateResponse> UpdateAsync(
        Guid accountId,
        Guid transportPlanId,
        Request.UpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.CreateResponse> GetByIdAsync(
        Guid accountId,
        Guid transportPlanId,
        CancellationToken cancellationToken = default);

    Task<Response.ListResponse> GetListAsync(
        Guid accountId,
        Request.ListRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.CreateResponse> CreateAsync(
        Guid accountId,
        Guid bookingId,
        Request.CreateRequest request,
        CancellationToken cancellationToken = default);
}
