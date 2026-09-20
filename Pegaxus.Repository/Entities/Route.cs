using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Route: Một tuyến đường có hướng nối từ một Location bắt đầu đến một Location kết thúc. 
/// Route có thể được tái sử dụng trong nhiều RoutePlan.
/// </summary>
public partial class Route
{
    public Guid RouteId { get; set; }

    public string RouteCode { get; set; } = null!;

    public Guid FromLocationId { get; set; }

    public Guid ToLocationId { get; set; }

    public TransportMode TransportMode { get; set; }

    public string? RouteName { get; set; }

    public decimal? DistanceKm { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    public string? RouteNote { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual Location FromLocation { get; set; } = null!;

    public virtual ICollection<RoutePlanRoute> RoutePlanRoutes { get; set; } = new List<RoutePlanRoute>();

    public virtual Location ToLocation { get; set; } = null!;
}
