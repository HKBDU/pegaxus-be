namespace Pegaxus.Service.RoutePlanService;

public interface IRoutePlanRealtimeService
{
    /// <summary>Tells the given accounts a route plan was cancelled. Call it only after the cancel has committed; a delivery failure must not surface to the caller.</summary>
    Task PublishCancelledAsync(IReadOnlyList<Guid> recipientAccountIds, Response.RoutePlanCancelledEvent cancelled, CancellationToken cancellationToken = default);
}

public sealed class NoopRoutePlanRealtimeService : IRoutePlanRealtimeService
{
    public Task PublishCancelledAsync(IReadOnlyList<Guid> recipientAccountIds, Response.RoutePlanCancelledEvent cancelled, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
