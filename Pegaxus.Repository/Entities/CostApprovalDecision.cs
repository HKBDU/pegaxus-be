using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// CostApprovalDecision: Quyết định của Employee đối với một EmergencyCostRequest, 
/// gồm kết quả duyệt, số tiền được duyệt và lý do quyết định.
/// </summary>
public partial class CostApprovalDecision
{
    public Guid CostApprovalDecisionId { get; set; }

    public Guid EmergencyCostRequestId { get; set; }

    public Guid EmployeeId { get; set; }

    public ApprovalDecision Decision { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public string? DecisionReason { get; set; }

    public DateTime DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual EmergencyCostRequest EmergencyCostRequest { get; set; } = null!;

    public virtual Employee Employee { get; set; } = null!;
}
