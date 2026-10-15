using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// AuthoritySubmission: Một lần thực tế gửi ComplianceDossier tới cơ quan có thẩm quyền. 
/// Một dossier có thể được gửi nhiều lần nếu bị trả về hoặc từ chối.
/// </summary>
public partial class AuthoritySubmission
{
    public Guid AuthoritySubmissionId { get; set; }

    public Guid ComplianceDossierId { get; set; }

    public Guid SubmittedByAccountId { get; set; }

    public int SubmissionNo { get; set; }

    public string AuthorityName { get; set; } = null!;

    public string? SubmissionReference { get; set; }

    public string? SubmissionMethod { get; set; }

    public AuthoritySubmissionStatus Status { get; set; } = AuthoritySubmissionStatus.DRAFT;

    public bool IsRequired { get; set; } = true;

    public string? PermitNumber { get; set; }

    public DateOnly? PermitIssuedDate { get; set; }

    public DateOnly? PermitExpiryDate { get; set; }

    public DateOnly? RevisionDueDate { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ResponseReceivedAt { get; set; }

    public string? DecisionNote { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ComplianceDossier ComplianceDossier { get; set; } = null!;

    public virtual Account SubmittedByAccount { get; set; } = null!;

    public virtual ICollection<AuthoritySubmissionEvidenceFile> EvidenceFiles { get; set; } = new List<AuthoritySubmissionEvidenceFile>();
}
