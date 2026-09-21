using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// AccountNotification: Bảng trung gian gán Notification cho Account 
/// và theo dõi trạng thái gửi, thời điểm nhận và thời điểm đọc của từng người nhận.
/// </summary>
public partial class AccountNotification
{
    public Guid AccountId { get; set; }

    public Guid NotificationId { get; set; }

    public RecipientStatus Status { get; set; } = RecipientStatus.PENDING;

    public DateTime? DeliveredAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual Notification Notification { get; set; } = null!;
}
