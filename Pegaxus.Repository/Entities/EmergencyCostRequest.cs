using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class EmergencyCostRequest
{
    public long EmergencyCostRequestId { get; set; }

    public long IncidentReportId { get; set; }

    public string RequestCode { get; set; } = null!;

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
