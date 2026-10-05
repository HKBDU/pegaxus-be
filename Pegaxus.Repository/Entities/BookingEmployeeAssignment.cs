using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

public sealed class BookingEmployeeAssignment
{
    public Guid BookingEmployeeAssignmentId { get; set; }
    public Guid BookingId { get; set; }
    public Guid EmployeeId { get; set; }
    public EmployeeRole AssignmentRole { get; set; }
    public Guid AssignedByEmployeeId { get; set; }
    public BookingAssignmentStatus Status { get; set; } = BookingAssignmentStatus.ACTIVE;
    public DateTime AssignedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Note { get; set; }

    public Booking Booking { get; set; } = null!;
    public Employee Employee { get; set; } = null!;
    public Employee AssignedByEmployee { get; set; } = null!;
}
