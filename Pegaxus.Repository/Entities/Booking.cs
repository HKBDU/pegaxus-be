using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Booking
{
    public long BookingId { get; set; }

    public string BookingCode { get; set; } = null!;

    public long CustomerId { get; set; }

    public long? AssignedEmployeeId { get; set; }

    public string OriginAddress { get; set; } = null!;

    public string OriginCountry { get; set; } = null!;

    public string DestinationAddress { get; set; } = null!;

    public string DestinationCountry { get; set; } = null!;

    public DateOnly RequestedDepartureDate { get; set; }

    public DateOnly? RequestedArrivalDate { get; set; }

    public string? SpecialRequirements { get; set; }

    public string? CustomerNote { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? RejectedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? RejectionReason { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Employee? AssignedEmployee { get; set; }

    public virtual ICollection<BookingHorse> BookingHorses { get; set; } = new List<BookingHorse>();

    public virtual ICollection<BookingStatusHistory> BookingStatusHistories { get; set; } = new List<BookingStatusHistory>();

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual Transport? Transport { get; set; }
}
