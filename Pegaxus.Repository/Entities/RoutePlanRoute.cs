using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// RoutePlanRoute: Bảng trung gian biểu diễn một Route được dùng trong một RoutePlan, 
/// gồm thứ tự đi, Driver phụ trách và thời gian dự kiến/thực tế của chính lần sử dụng Route đó.
/// </summary>
public partial class RoutePlanRoute
{
    public Guid RoutePlanRouteId { get; set; }

    public Guid RoutePlanId { get; set; }

    public Guid RouteId { get; set; }

    public int SequenceNo { get; set; }

    public RouteType RouteType { get; set; } = RouteType.PRIMARY;

    public Guid? DriverEmployeeId { get; set; }

    public Guid? EscortEmployeeId { get; set; }

    public Guid? TransportProviderId { get; set; }

    public DateTime? PlannedDepartureAt { get; set; }

    public DateTime? PlannedArrivalAt { get; set; }

    public DateTime? ActualDepartureAt { get; set; }

    public DateTime? ActualArrivalAt { get; set; }

    public string? Note { get; set; }

    public string? ServiceType { get; set; }
    public string? ProviderReference { get; set; }
    public DateTime? SupersededAt { get; set; }
    public Guid? SupersededByChangeRequestId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Employee? DriverEmployee { get; set; }

    public virtual Employee? EscortEmployee { get; set; }

    public virtual ICollection<HandoverRecord> HandoverRecordFromRoutePlanRoutes { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<HandoverRecord> HandoverRecordToRoutePlanRoutes { get; set; } = new List<HandoverRecord>();

    public virtual Route Route { get; set; } = null!;

    public virtual RoutePlan RoutePlan { get; set; } = null!;

    public virtual TransportProvider? TransportProvider { get; set; }

    public virtual ICollection<RoutePlanRouteVehicle> RoutePlanRouteVehicles { get; set; } = new List<RoutePlanRouteVehicle>();
}
