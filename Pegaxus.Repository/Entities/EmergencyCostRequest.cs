using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// EmergencyCostRequest: Yêu cầu chi phí khẩn cấp phát sinh từ IncidentReport, 
/// được Employee phê duyệt hoặc từ chối trước khi đưa vào InvoiceItem.
/// </summary>
public partial class EmergencyCostRequest
{
    public Guid EmergencyCostRequestId { get; set; }

    public Guid IncidentReportId { get; set; }

    public string RequestCode { get; set; } = null!;

    public EmergencyCostCategory Category { get; set; }

    public EmergencyCostStatus Status { get; set; } = EmergencyCostStatus.PENDING;

    public string Description { get; set; } = null!;

    public decimal RequestedAmount { get; set; }

    public string Currency { get; set; } = null!;

    public string Justification { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CostApprovalDecision? CostApprovalDecision { get; set; }

    public virtual IncidentReport IncidentReport { get; set; } = null!;

    public virtual InvoiceItem? InvoiceItem { get; set; }
}
