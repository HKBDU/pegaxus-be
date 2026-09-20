using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Notification
{
    public Guid NotificationId { get; set; }

    public Guid TransportId { get; set; }

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
