using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status.
/// </summary>
public partial class BookingStatusHistory
{
    public long BookingStatusHistoryId { get; set; }

    public long BookingId { get; set; }

    public long ChangedByAccountId { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Account ChangedByAccount { get; set; } = null!;
}
