using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// ComplianceDossier: Bộ hồ sơ pháp lý thực tế của một chuyến đi, tức của một Transport cụ thể.
/// </summary>
public partial class ComplianceDossier
{
    public long ComplianceDossierId { get; set; }

    public string DossierCode { get; set; } = null!;

    public long TransportId { get; set; }

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
