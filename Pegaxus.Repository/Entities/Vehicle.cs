using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Vehicle: Phương tiện vận chuyển master dùng chung cho ROAD, AIR hoặc SEA, ví dụ xe tải chở ngựa, máy bay hoặc tàu. Vehicle không thuộc cố định một TransportPlan; phương tiện được gán vào từng chặng RoutePlanRoute.
/// </summary>
public partial class Vehicle
{
    public long VehicleId { get; set; }

    public string VehicleCode { get; set; } = null!;

    public string? VehicleName { get; set; }

    public string? LicensePlate { get; set; }

    public string? VehicleType { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public int? CapacityHorses { get; set; }

    public string? RegistrationNumber { get; set; }

    public DateOnly? RegistrationExpiryDate { get; set; }

    public string? InsuranceNumber { get; set; }

    public DateOnly? InsuranceExpiryDate { get; set; }

    public string? SpecialEquipment { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public long? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<RoutePlanRouteVehicle> RoutePlanRouteVehicles { get; set; } = new List<RoutePlanRouteVehicle>();

    public virtual ICollection<VehicleIncidentDetail> VehicleIncidentDetails { get; set; } = new List<VehicleIncidentDetail>();
}
