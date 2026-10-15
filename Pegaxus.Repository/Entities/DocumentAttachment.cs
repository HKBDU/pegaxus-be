namespace Pegaxus.Repository.Entities;

/// <summary>
/// DocumentAttachment: Một file cụ thể thuộc cùng một lần nộp giấy tờ.
/// </summary>
public partial class DocumentAttachment
{
    public Guid DocumentAttachmentId { get; set; }

    public Guid DocumentVersionId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string? MimeType { get; set; }

    public long? FileSizeBytes { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual DocumentVersion DocumentVersion { get; set; } = null!;
}
