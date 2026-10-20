namespace Pegaxus.Service.LocationTrackingService;

public interface ILocationTrackingService
{
    Task<Response.Snapshot> GetAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
    Task EnsureViewerAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.Accepted> RecordAsync(Guid transportId, Request.LocationRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.ArrivalConfirmed> ConfirmArrivalAsync(Guid transportId, Guid routePlanRouteId, Request.ArrivalRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.PermissionDenied> PermissionDeniedAsync(Guid transportId, Guid accountId, CancellationToken cancellationToken = default);
}

public interface ILocationTrackingRealtimeService
{
    Task PublishAsync(Response.LocationUpdate location, IReadOnlyList<Guid> viewerAccountIds, CancellationToken cancellationToken = default);
}
