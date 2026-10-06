using Pegaxus.Repository.Enum;
using System.Text.Json;

namespace Pegaxus.Repository.Entities;

public partial class QuarantineProof
{
    public Guid QuarantineProofId { get; set; }

    public Guid ComplianceDossierId { get; set; }

    public QuarantineProofStatus Status { get; set; } = QuarantineProofStatus.PendingVerification;

    public Guid SubmittedByEmployeeId { get; set; }

    public Guid? VerifiedByEmployeeId { get; set; }

    public Guid? LocationId { get; set; }

    public string BcpName { get; set; } = null!;

    public string? OfficialVeterinarianName { get; set; }

    public string? BcpReceiptNumber { get; set; }

    public string? SubmittedNote { get; set; }

    public JsonDocument? MicrochipChecklist { get; set; }

    public DateTime? InspectionAt { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? Conditions { get; set; }

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
