using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// ComplianceDossier: Bộ hồ sơ pháp lý thực tế của một chuyến đi, 
/// tức của một Transport cụ thể.
/// </summary>
public partial class ComplianceDossier
{
    public Guid ComplianceDossierId { get; set; }

    public string DossierCode { get; set; } = null!;

    public Guid TransportId { get; set; }

    public Guid? DossierTemplateId { get; set; }

    public virtual DossierTemplate? DossierTemplate { get; set; }

    public ComplianceDossierStatus Status { get; set; } = ComplianceDossierStatus.DRAFT;

    public DateTime OpenedAt { get; set; }

    public DateTime? ReadyForSubmissionAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? RejectedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public string? RejectionReason { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AuthoritySubmission> AuthoritySubmissions { get; set; } = new List<AuthoritySubmission>();

    public virtual ICollection<DossierRequirement> DossierRequirements { get; set; } = new List<DossierRequirement>();

    public virtual Transport Transport { get; set; } = null!;
}
