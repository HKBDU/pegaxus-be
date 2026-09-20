using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// TransportPlanProvider: Bảng trung gian N-N gán TransportProvider vào TransportPlan, 
/// lưu loại dịch vụ, thời điểm phân công và ghi chú hợp tác.
/// </summary>
public partial class TransportPlanProvider
{
    public Guid TransportPlanId { get; set; }

    public Guid TransportProviderId { get; set; }

    public string? ServiceType { get; set; }

    public DateTime AssignedAt { get; set; }

    public string? Note { get; set; }

    public virtual TransportPlan TransportPlan { get; set; } = null!;

    public virtual TransportProvider TransportProvider { get; set; } = null!;
}
