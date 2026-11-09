using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// BookingHorse: Bảng trung gian N-N gán Horse vào Booking 
/// và lưu yêu cầu xử lý riêng cho từng con ngựa trong chuyến đặt.
/// </summary>
public partial class BookingHorse
{
    public Guid BookingId { get; set; }

    public Guid HorseId { get; set; }

    public string? SpecialHandlingNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the horse was left behind mid-trip (it could not continue); the rest of the booking travels on.</summary>
    public DateTime? DetachedAt { get; set; }

    public Guid? DetachedIncidentReportId { get; set; }

    /// <summary>The route-plan stop where the horse was left in the care of a local veterinary facility.</summary>
    public Guid? DetachedLocationId { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Horse Horse { get; set; } = null!;

    public virtual IncidentReport? DetachedIncidentReport { get; set; }

    public virtual Location? DetachedLocation { get; set; }
}
