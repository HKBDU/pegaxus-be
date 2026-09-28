using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// ComplianceRequirement: Danh mục master các loại yêu cầu pháp lý 
/// hoặc loại giấy tờ mà hệ thống biết có thể cần trong hồ sơ.
/// </summary>
public partial class ComplianceRequirement
{
    public Guid ComplianceRequirementId { get; set; }

    public string RequirementCode { get; set; } = null!;

    public string RequirementName { get; set; } = null!;

    public ComplianceRequirementStatus Status { get; set; } = ComplianceRequirementStatus.ACTIVE;

    public string? Description { get; set; }

    public string? IssuingAuthority { get; set; }

    public string? Country { get; set; }

    public ComplianceRequirementCategory Category { get; set; }

    public int? ValidityPeriodDays { get; set; }

    public bool ValidityRequired { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<DossierRequirement> DossierRequirements { get; set; } = new List<DossierRequirement>();

    public virtual ICollection<DossierTemplateItem> DossierTemplateItems { get; set; } = new List<DossierTemplateItem>();
}
