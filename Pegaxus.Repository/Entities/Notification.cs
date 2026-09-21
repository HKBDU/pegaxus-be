using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Notification: Thông báo phát sinh trong một Transport, 
/// chứa loại, mức ưu tiên, nội dung và tham chiếu nghiệp vụ; 
/// người nhận được quản lý qua AccountNotification.
/// </summary>
public partial class Notification
{
    public Guid NotificationId { get; set; }

    public Guid TransportId { get; set; }

    public NotificationType NotificationType { get; set; }

    public NotificationPriority Priority { get; set; } = NotificationPriority.NORMAL;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? ReferenceType { get; set; }

    public Guid? ReferenceId { get; set; }

    public string? ActionUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public virtual ICollection<AccountNotification> AccountNotifications { get; set; } = new List<AccountNotification>();

    public virtual Transport Transport { get; set; } = null!;
}
