using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.NotificationService;

/// <summary>
/// Last-line protection for in-app notification copy. Business code may use technical vocabulary internally, but the
/// persisted title and message are read directly by end users in the notification centre.
/// </summary>
public static class UserFacingNotificationText
{
    public static string Title(NotificationType type, NotificationPriority priority, string value) =>
        UserFacingText.ContainsInternalTerms(value)
            ? priority is NotificationPriority.HIGH or NotificationPriority.URGENT
                ? $"Important {TypeLabel(type)} update"
                : $"{TypeLabel(type)} update"
            : value;

    public static string Message(string value) =>
        UserFacingText.ContainsInternalTerms(value)
            ? "Open this notification to view the latest details."
            : value;

    private static string TypeLabel(NotificationType type) => type switch
    {
        NotificationType.BOOKING => "booking",
        NotificationType.TRANSPORT => "transport",
        NotificationType.ROUTE => "route",
        NotificationType.HANDOVER => "handover",
        NotificationType.HORSE_HEALTH => "horse health",
        NotificationType.INCIDENT => "incident",
        NotificationType.EMERGENCY_COST => "emergency cost",
        NotificationType.PAYMENT => "payment",
        NotificationType.CLAIM => "claim",
        NotificationType.COMPLIANCE => "document",
        NotificationType.DOCUMENT => "document",
        _ => "system"
    };
}
