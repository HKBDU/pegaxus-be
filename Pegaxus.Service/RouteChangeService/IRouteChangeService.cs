namespace Pegaxus.Service.RouteChangeService;

public interface IRouteChangeService
{
    Task<Response.Created> CreateAsync(Guid routePlanId, Guid accountId, Request.CreateRequest request, CancellationToken cancellationToken = default);
    Task<Response.DecisionResult> DecideAsync(Guid routePlanId, Guid accountId, Request.DecisionRequest request, CancellationToken cancellationToken = default);
    Task<Response.ActivationResult> ActivateReturnAsync(Guid routePlanId, Guid accountId, Request.ActivationRequest request, CancellationToken cancellationToken = default);
    Task<Response.DriverNoticeResult> NotifyDriversAsync(Guid routePlanId, Guid accountId, Request.DriverNoticeRequest request, CancellationToken cancellationToken = default);
}
