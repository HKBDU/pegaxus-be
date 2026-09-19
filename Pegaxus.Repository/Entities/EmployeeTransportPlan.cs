using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class EmployeeTransportPlan
{
    public long EmployeeId { get; set; }

    public long TransportPlanId { get; set; }

    public string? Responsibility { get; set; }

    public DateTime AssignedAt { get; set; }

    public bool IsPrimary { get; set; }

    public string? Note { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual TransportPlan TransportPlan { get; set; } = null!;
}
