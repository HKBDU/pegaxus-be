namespace Pegaxus.Service.NotificationService;

public static class Request
{
    public sealed class NotificationListRequest
    {
        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public bool? UnreadOnly { get; set; }
    }
}
