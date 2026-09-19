using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class CostApprovalDecision
{
    public long CostApprovalDecisionId { get; set; }

    public long EmergencyCostRequestId { get; set; }

    public long EmployeeId { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public string? DecisionReason { get; set; }

    public DateTime DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual EmergencyCostRequest EmergencyCostRequest { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
