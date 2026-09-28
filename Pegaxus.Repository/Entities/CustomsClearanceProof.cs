using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

public partial class CustomsClearanceProof
{
    public Guid CustomsClearanceProofId { get; set; }

    public Guid ComplianceDossierId { get; set; }

    public CustomsClearanceProofStatus Status { get; set; } = CustomsClearanceProofStatus.PendingVerification;

    public CustomsLane? CustomsLane { get; set; }

    public Guid SubmittedByEmployeeId { get; set; }

    public Guid? VerifiedByEmployeeId { get; set; }

    public Guid? LocationId { get; set; }

    public string CustomsPortName { get; set; } = null!;

    public string? MrnNumber { get; set; }

    public DateTime? ClearanceAt { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? VerificationNote { get; set; }

    public string? RejectionReason { get; set; }

    public bool IsFinalized { get; set; }

    public DateTime? FinalizedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ComplianceDossier ComplianceDossier { get; set; } = null!;

    public virtual Employee SubmittedByEmployee { get; set; } = null!;

    public virtual Employee? VerifiedByEmployee { get; set; }

    public virtual ICollection<ProofAttachment> Attachments { get; set; } = new List<ProofAttachment>();
}
