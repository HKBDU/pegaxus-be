using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Transport
{
    public long TransportId { get; set; }

    public string TransportCode { get; set; } = null!;

    public long BookingId { get; set; }

    public DateTime? ActualDepartureAt { get; set; }

    public DateTime? ActualArrivalAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual ComplianceDossier? ComplianceDossier { get; set; }

    public virtual FinancialReport? FinancialReport { get; set; }

    public virtual ICollection<HandoverRecord> HandoverRecords { get; set; } = new List<HandoverRecord>();

    public virtual ICollection<HorseHealthLog> HorseHealthLogs { get; set; } = new List<HorseHealthLog>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<TransportPlan> TransportPlans { get; set; } = new List<TransportPlan>();
}
