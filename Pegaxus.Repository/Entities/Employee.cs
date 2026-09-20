using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Employee
{
    public Guid EmployeeId { get; set; }

    public Guid AccountId { get; set; }

    public string EmployeeCode { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public virtual ICollection<CostApprovalDecision> CostApprovalDecisions { get; set; } = new List<CostApprovalDecision>();

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<EmployeeTransportPlan> EmployeeTransportPlans { get; set; } = new List<EmployeeTransportPlan>();

    public virtual ICollection<HandoverRecord> HandoverRecordFromEmployees { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<HandoverRecord> HandoverRecordToEmployees { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<HorseHealthLog> HorseHealthLogs { get; set; } = new List<HorseHealthLog>();

    public virtual ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<RoutePlanRoute> RoutePlanRoutes { get; set; } = new List<RoutePlanRoute>();

    public virtual ICollection<TransportPlan> TransportPlans { get; set; } = new List<TransportPlan>();
}
