using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class VehicleIncidentDetail
{
    public long VehicleIncidentDetailId { get; set; }

    public long IncidentReportId { get; set; }

    public long VehicleId { get; set; }

    public string? VehicleCondition { get; set; }

    public string? DamageDescription { get; set; }

    public bool VehicleOperable { get; set; }

    public bool RepairRequired { get; set; }

    public bool TowingRequired { get; set; }

    public bool ReplacementVehicleRequired { get; set; }

    public decimal? EstimatedRepairCost { get; set; }

    public string? RepairNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual IncidentReport IncidentReport { get; set; } = null!;

    public virtual Vehicle Vehicle { get; set; } = null!;
}
