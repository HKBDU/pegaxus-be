using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DocumentReview: Kết quả kiểm tra của một DocumentVersion cụ thể, ví dụ APPROVED, REJECTED hoặc REVISION_REQUIRED.
/// </summary>
public partial class DocumentReview
{
    public Guid DocumentReviewId { get; set; }

    public Guid DocumentVersionId { get; set; }

    public string? ReviewComment { get; set; }

    public DateTime ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual DocumentVersion DocumentVersion { get; set; } = null!;
}
