using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Claim: Khiếu nại do Customer gửi liên quan đến một lần bàn giao. 
/// Hệ thống chỉ lưu khiếu nại và kết quả; 
/// việc trao đổi/thương lượng giữa Customer và Manager diễn ra ngoài hệ thống.
/// </summary>
public partial class Claim
{
    public Guid ClaimId { get; set; }

    public string ClaimCode { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public Guid HandoverRecordId { get; set; }

    public ClaimType ClaimType { get; set; }

    public ClaimStatus Status { get; set; } = ClaimStatus.PENDING;

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
