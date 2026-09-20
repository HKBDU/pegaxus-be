using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class TransportPlan
{
    public Guid TransportPlanId { get; set; }

    public Guid TransportId { get; set; }

    public string PlanCode { get; set; } = null!;

    public DateTime? PlannedStartAt { get; set; }

    public DateTime? PlannedEndAt { get; set; }

    public string? PlanningNote { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    public decimal? EstimatedDistanceKm { get; set; }

    public Guid? ApprovedByEmployeeId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Employee? ApprovedByEmployee { get; set; }

    public virtual ICollection<EmployeeTransportPlan> EmployeeTransportPlans { get; set; } = new List<EmployeeTransportPlan>();

    public virtual RoutePlan? RoutePlan { get; set; }

    public virtual Transport Transport { get; set; } = null!;

    public virtual ICollection<TransportPlanProvider> TransportPlanProviders { get; set; } = new List<TransportPlanProvider>();
}
