using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// IncidentReport: Báo cáo sự cố phát sinh trong quá trình vận chuyển. category gồm VEHICLE, HORSE_HEALTH hoặc BOTH.
/// </summary>
public partial class IncidentReport
{
    public Guid IncidentReportId { get; set; }

    public string IncidentCode { get; set; } = null!;

    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public DateTime ReportedAt { get; set; }

    public string? ImmediateAction { get; set; }

    public string? ResolutionSummary { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<EmergencyCostRequest> EmergencyCostRequests { get; set; } = new List<EmergencyCostRequest>();

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<HorseHealthIncidentDetail> HorseHealthIncidentDetails { get; set; } = new List<HorseHealthIncidentDetail>();

    public virtual Location Location { get; set; } = null!;

    public virtual VehicleIncidentDetail? VehicleIncidentDetail { get; set; }
}
