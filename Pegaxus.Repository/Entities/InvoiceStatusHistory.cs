using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// InvoiceStatusHistory: Lịch sử thay đổi trạng thái Invoice. 
/// type cho biết hành động nghiệp vụ gây ra thay đổi; 
/// changed_by_account_id có thể null nếu hệ thống tự động thực hiện, ví dụ MARK_OVERDUE.
/// </summary>
public partial class InvoiceStatusHistory
{
    public Guid InvoiceStatusHistoryId { get; set; }

    public Guid InvoiceId { get; set; }

    public InvoiceHistoryType Type { get; set; }

    public InvoiceStatus? PreviousStatus { get; set; }

    public InvoiceStatus NewStatus { get; set; }

    public Guid? ChangedByAccountId { get; set; }

    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Account? ChangedByAccount { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
