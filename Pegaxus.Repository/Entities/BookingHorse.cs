using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class BookingHorse
{
    public long BookingId { get; set; }

    public long HorseId { get; set; }

    public string? SpecialHandlingNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Booking Booking { get; set; } = null!;

    public virtual Horse Horse { get; set; } = null!;
}
