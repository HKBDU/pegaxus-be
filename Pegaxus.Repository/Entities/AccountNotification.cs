using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class AccountNotification
{
    public long AccountId { get; set; }

    public long NotificationId { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Notification Notification { get; set; } = null!;
}
