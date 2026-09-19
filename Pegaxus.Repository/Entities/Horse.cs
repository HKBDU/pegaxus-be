using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class Horse
{
    public long HorseId { get; set; }

    public long CustomerId { get; set; }

    public string HorseName { get; set; } = null!;

    public string? PassportNumber { get; set; }

    public string? MicrochipNumber { get; set; }

    public string? Breed { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Color { get; set; }

    public string? CountryOfOrigin { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? HeightCm { get; set; }

    public string? IdentificationNotes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public long? DeletedByAccountId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<BookingHorse> BookingHorses { get; set; } = new List<BookingHorse>();

    public virtual Customer Customer { get; set; } = null!;

    public virtual Account? DeletedByAccount { get; set; }

    public virtual ICollection<HorseHealthIncidentDetail> HorseHealthIncidentDetails { get; set; } = new List<HorseHealthIncidentDetail>();

    public virtual ICollection<HorseHealthLog> HorseHealthLogs { get; set; } = new List<HorseHealthLog>();

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
}
