using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// BookingStatusHistory: Lịch sử thay đổi trạng thái Booking. 
/// type cho biết hành động nghiệp vụ nào đã gây ra thay đổi status.
/// </summary>
public partial class BookingStatusHistory
{
    public Guid BookingStatusHistoryId { get; set; }

    public Guid BookingId { get; set; }

    public BookingHistoryType Type { get; set; }

    public BookingStatus? PreviousStatus { get; set; }

    public BookingStatus NewStatus { get; set; }

    public Guid ChangedByAccountId { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Account ChangedByAccount { get; set; } = null!;
}
