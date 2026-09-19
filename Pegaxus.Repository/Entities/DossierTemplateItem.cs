using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DossierTemplateItem: Một dòng yêu cầu nằm trong DossierTemplate, dùng để liên kết Template với ComplianceRequirement và đánh dấu yêu cầu bắt buộc hay không.
/// </summary>
public partial class DossierTemplateItem
{
    public long DossierTemplateItemId { get; set; }

    public long DossierTemplateId { get; set; }

    public long ComplianceRequirementId { get; set; }

    public int SequenceNo { get; set; }

    public bool IsMandatory { get; set; }

    public string? Instruction { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ComplianceRequirement ComplianceRequirement { get; set; } = null!;

    public virtual DossierTemplate DossierTemplate { get; set; } = null!;
}
