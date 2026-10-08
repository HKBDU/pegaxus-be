namespace Pegaxus.Service.RouteOutsourcingService;

public interface IService
{
    Task<Response> SetAsync(Guid accountId, Guid routePlanId, Guid segmentId, Request request, CancellationToken cancellationToken = default);
}
