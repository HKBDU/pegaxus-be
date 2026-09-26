using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DocumentVersion: Một phiên bản file cụ thể của ComplianceDocument. 
/// Khi giấy tờ bị sửa hoặc upload lại thì tạo version mới, không tạo ComplianceDocument mới.
/// </summary>
public partial class DocumentVersion
{
    public Guid DocumentVersionId { get; set; }

    public Guid ComplianceDocumentId { get; set; }

    public int VersionNo { get; set; }

    public string FileName { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string? MimeType { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? UploadNote { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ComplianceDocument ComplianceDocument { get; set; } = null!;

    public virtual ICollection<DocumentReview> DocumentReviews { get; set; } = new List<DocumentReview>();

    public virtual ICollection<DocumentAttachment> DocumentAttachments { get; set; } = new List<DocumentAttachment>();
}
