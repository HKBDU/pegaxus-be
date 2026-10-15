using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// EmployeeTransportPlan: Bảng trung gian N-N gán Employee vào TransportPlan, 
/// lưu trách nhiệm, thời điểm phân công và người phụ trách chính.
/// </summary>
public partial class EmployeeTransportPlan
{
    public Guid EmployeeId { get; set; }

    public Guid TransportPlanId { get; set; }

    public string? Responsibility { get; set; }

    public DateTime AssignedAt { get; set; }

    public bool IsPrimary { get; set; }

    public string? Note { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual TransportPlan TransportPlan { get; set; } = null!;
}
