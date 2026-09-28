namespace Pegaxus.Repository.Entities;

public partial class ProofAttachment
{
    public Guid ProofAttachmentId { get; set; }

    public Guid? QuarantineProofId { get; set; }

    public Guid? CustomsClearanceProofId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string? MimeType { get; set; }

    public long? FileSizeBytes { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual QuarantineProof? QuarantineProof { get; set; }

    public virtual CustomsClearanceProof? CustomsClearanceProof { get; set; }
}
