using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// RoutePlan: Kế hoạch tuyến đường của một TransportPlan. Đây là tập hợp các Route được sắp xếp nối tiếp từ điểm đầu đến điểm cuối.
/// </summary>
public partial class RoutePlan
{
    public long RoutePlanId { get; set; }

    public long TransportPlanId { get; set; }

    public string RoutePlanCode { get; set; } = null!;

    public decimal? TotalDistanceKm { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    public string? RouteNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<RoutePlanRoute> RoutePlanRoutes { get; set; } = new List<RoutePlanRoute>();

    public virtual TransportPlan TransportPlan { get; set; } = null!;
}
