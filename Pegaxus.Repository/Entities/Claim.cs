using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. Hệ thống chỉ lưu khiếu nại và kết quả; việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống.
/// </summary>
public partial class Claim
{
    public long ClaimId { get; set; }

    public string ClaimCode { get; set; } = null!;

    public long CustomerId { get; set; }

    public long HandoverRecordId { get; set; }

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal? RequestedCompensationAmount { get; set; }

    public string? Currency { get; set; }

    public string? EvidenceNote { get; set; }

    public string? CustomerNote { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ClaimResolution? ClaimResolution { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual HandoverRecord HandoverRecord { get; set; } = null!;
}
