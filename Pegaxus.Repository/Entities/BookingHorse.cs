using System;
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

    public virtual Booking Booking { get; set; } = null!;

    public virtual Horse Horse { get; set; } = null!;
}
