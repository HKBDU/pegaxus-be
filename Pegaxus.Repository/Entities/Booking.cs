using System;
using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// Booking: Yêu cầu vận chuyển ngựa do Customer tạo, gồm điểm đi/đến, 
/// thời gian mong muốn, các Horse cần vận chuyển và Employee được phân công xử lý.
/// </summary>
public partial class Booking
{
    public Guid BookingId { get; set; }

    public string BookingCode { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public Guid? AssignedEmployeeId { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.PENDING;

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

    public virtual ICollection<BookingEmployeeAssignment> EmployeeAssignments { get; set; } = new List<BookingEmployeeAssignment>();

    public virtual ICollection<BookingStatusHistory> BookingStatusHistories { get; set; } = new List<BookingStatusHistory>();

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual Transport? Transport { get; set; }
}
