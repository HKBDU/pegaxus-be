using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// DocumentRequest: Yêu cầu gửi tới Customer để cung cấp, sửa, upload lại hoặc 
/// bổ sung thông tin cho một DossierRequirement.
/// </summary>
public partial class DocumentRequest
{
    public Guid DocumentRequestId { get; set; }

    public Guid DossierRequirementId { get; set; }

    public Guid RequestedByAccountId { get; set; }

    public string RequestCode { get; set; } = null!;

    public DocumentRequestType RequestType { get; set; }

    public DocumentRequestStatus Status { get; set; } = DocumentRequestStatus.OPEN;

    public string Message { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateTime? FulfilledAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DossierRequirement DossierRequirement { get; set; } = null!;

    public virtual Account RequestedByAccount { get; set; } = null!;
}
