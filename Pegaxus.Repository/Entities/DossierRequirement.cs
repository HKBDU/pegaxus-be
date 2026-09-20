using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DossierRequirement: Một yêu cầu pháp lý thực tế đã được sinh ra trong ComplianceDossier 
/// của chuyến đi cụ thể, dựa trên loại ComplianceRequirement.
/// </summary>
public partial class DossierRequirement
{
    public Guid DossierRequirementId { get; set; }

    public Guid ComplianceDossierId { get; set; }

    public Guid ComplianceRequirementId { get; set; }

    public DossierRequirementStatus Status { get; set; } = DossierRequirementStatus.PENDING;

    public bool IsMandatory { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Instruction { get; set; }

    public DateTime? SatisfiedAt { get; set; }

    public string? WaivedReason { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ComplianceDocument? ComplianceDocument { get; set; }

    public virtual ComplianceDossier ComplianceDossier { get; set; } = null!;

    public virtual ComplianceRequirement ComplianceRequirement { get; set; } = null!;

    public virtual ICollection<DocumentRequest> DocumentRequests { get; set; } = new List<DocumentRequest>();
}
