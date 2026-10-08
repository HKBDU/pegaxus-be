namespace Pegaxus.Service.RouteChangeService;

public interface IService
{
    Task<Response.Created> CreateAsync(Guid routePlanId, Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default);
    Task<Response.ListResponse> ListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.Detail> GetAsync(Guid changeRequestId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.DecisionResult> DecideAsync(Guid changeRequestId, Guid accountId, Request.DecisionRequest request, CancellationToken cancellationToken = default);
}
