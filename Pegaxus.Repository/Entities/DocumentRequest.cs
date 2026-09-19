using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc bổ sung thông tin cho một DossierRequirement.
/// </summary>
public partial class DocumentRequest
{
    public long DocumentRequestId { get; set; }

    public long DossierRequirementId { get; set; }

    public string RequestCode { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateTime? FulfilledAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DossierRequirement DossierRequirement { get; set; } = null!;
}
