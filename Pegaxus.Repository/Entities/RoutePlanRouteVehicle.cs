using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// RoutePlanRouteVehicle: Bảng trung gian N-N gán Vehicle vào một chặng RoutePlanRoute cụ thể. 
/// Nhờ vậy một Transport có thể đi xe -&gt; máy bay -&gt; xe, 
/// và cùng một Vehicle có thể được tái sử dụng ở nhiều chuyến/chặng khác nhau.
/// </summary>
public partial class RoutePlanRouteVehicle
{
    public Guid RoutePlanRouteId { get; set; }

    public Guid VehicleId { get; set; }

    public DateTime AssignedAt { get; set; }

    public string? Note { get; set; }

    public virtual RoutePlanRoute RoutePlanRoute { get; set; } = null!;

    public virtual Vehicle Vehicle { get; set; } = null!;
}
