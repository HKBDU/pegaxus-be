using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// ClaimResolution: Kết quả xử lý/thỏa thuận cuối cùng của một Claim, 
/// ví dụ mức bồi thường thực tế sau khi hai bên trao đổi trực tiếp.
/// </summary>
public partial class ClaimResolution
{
    public Guid ClaimResolutionId { get; set; }

    public Guid ClaimId { get; set; }

    public ClaimResolutionType ResolutionType { get; set; }

    public string ResolutionSummary { get; set; } = null!;

    public decimal? ApprovedCompensationAmount { get; set; }

    public string? Currency { get; set; }

    public string? CorrectiveAction { get; set; }

    public string? InternalNote { get; set; }

    public DateTime ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Claim Claim { get; set; } = null!;
}
