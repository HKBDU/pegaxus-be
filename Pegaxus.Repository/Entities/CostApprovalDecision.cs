using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class CostApprovalDecision
{
    public Guid CostApprovalDecisionId { get; set; }

    public Guid EmergencyCostRequestId { get; set; }

    public Guid EmployeeId { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public string? DecisionReason { get; set; }

    public DateTime DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual EmergencyCostRequest EmergencyCostRequest { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
