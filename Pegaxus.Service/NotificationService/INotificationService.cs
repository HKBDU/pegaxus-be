using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.NotificationService;

public interface INotificationService
{
    void Queue(NotificationCreateRequest request);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<Response.NotificationListResponse> GetAsync(
        Guid accountId,
        Request.NotificationListRequest request,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task MarkReadAsync(
        Guid accountId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task AcknowledgeDeliveredAsync(
        Guid accountId,
        Guid notificationId,
        CancellationToken cancellationToken = default);
}

public interface INotificationRealtimeService
{
    Task SendToAccountAsync(
        Guid accountId,
        NotificationRealtimeDto notification,
        CancellationToken cancellationToken = default);
}

public sealed class NoopNotificationRealtimeService : INotificationRealtimeService
{
    public Task SendToAccountAsync(
        Guid accountId,
        NotificationRealtimeDto notification,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class NotificationCreateRequest
{
    public Guid AccountId { get; init; }

    public Guid? TransportId { get; init; }

    public NotificationType Type { get; init; }

    public NotificationPriority Priority { get; init; } = NotificationPriority.NORMAL;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? ReferenceType { get; init; }

    public Guid? ReferenceId { get; init; }

    public string? ActionUrl { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class NotificationRealtimeDto
{
    public Guid Id { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Priority { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public string? ReferenceType { get; init; }

    public Guid? ReferenceId { get; init; }
}
