using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DossierTemplate: Bộ hồ sơ pháp lý mẫu cho một kịch bản vận chuyển. Template xác định thông thường cần những loại yêu cầu nào.
/// </summary>
public partial class DossierTemplate
{
    public long DossierTemplateId { get; set; }

    public string TemplateCode { get; set; } = null!;

    public string TemplateName { get; set; } = null!;

    public string? Description { get; set; }

    public int VersionNo { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public long? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<DossierTemplateItem> DossierTemplateItems { get; set; } = new List<DossierTemplateItem>();
}
