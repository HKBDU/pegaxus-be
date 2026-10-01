namespace Pegaxus.Service.NotificationService;

public static class Response
{
    public sealed class NotificationListResponse
    {
        public IReadOnlyList<NotificationItemResponse> Items { get; init; } = [];

        public int PageIndex { get; init; }

        public int PageSize { get; init; }

        public int TotalItems { get; init; }

        public int TotalPages { get; init; }
    }

    public sealed class NotificationItemResponse
    {
        public Guid Id { get; init; }

        public string Type { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string Priority { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset? DeliveredAt { get; init; }

        public DateTimeOffset? ReadAt { get; init; }

        public string? ReferenceType { get; init; }

        public Guid? ReferenceId { get; init; }

        public string? ActionUrl { get; init; }
    }
}
