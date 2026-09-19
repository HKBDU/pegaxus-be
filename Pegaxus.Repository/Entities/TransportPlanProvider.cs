using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class TransportPlanProvider
{
    public long TransportPlanId { get; set; }

    public long TransportProviderId { get; set; }

    public string? ServiceType { get; set; }

    public DateTime AssignedAt { get; set; }

    public string? Note { get; set; }

    public virtual TransportPlan TransportPlan { get; set; } = null!;

    public virtual TransportProvider TransportProvider { get; set; } = null!;
}
