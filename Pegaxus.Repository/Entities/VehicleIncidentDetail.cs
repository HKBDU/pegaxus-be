using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// VehicleIncidentDetail: Chi tiết phương tiện của một IncidentReport, 
/// gồm loại sự cố, tình trạng vận hành, nhu cầu sửa chữa, kéo xe, thay thế và 
/// chi phí ước tính.
/// </summary>
public partial class VehicleIncidentDetail
{
    public Guid VehicleIncidentDetailId { get; set; }

    public Guid IncidentReportId { get; set; }

    public Guid VehicleId { get; set; }

    public VehicleIncidentType IncidentType { get; set; }

    public string? VehicleCondition { get; set; }

    public string? DamageDescription { get; set; }

    public bool VehicleOperable { get; set; }

    public bool RepairRequired { get; set; }

    public bool TowingRequired { get; set; }

    public bool ReplacementVehicleRequired { get; set; }

    public decimal? EstimatedRepairCost { get; set; }

    public decimal? EstimatedRepairHours { get; set; }

    public string? DriverSafetyStatus { get; set; }

    public string? CargoConditionNote { get; set; }

    public string? RepairNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual IncidentReport IncidentReport { get; set; } = null!;

    public virtual ICollection<IncidentPhoto> IncidentPhotos { get; set; } = new List<IncidentPhoto>();

    public virtual Vehicle Vehicle { get; set; } = null!;
}
