using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

public partial class AuthoritySubmissionEvidenceFile
{
    public Guid AuthoritySubmissionEvidenceFileId { get; set; }

    public Guid AuthoritySubmissionId { get; set; }

    public AuthorityEvidenceType EvidenceType { get; set; }

    public Guid UploadedByEmployeeId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string? MimeType { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual AuthoritySubmission AuthoritySubmission { get; set; } = null!;

    public virtual Employee UploadedByEmployee { get; set; } = null!;
}
