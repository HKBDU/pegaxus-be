using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class HorseHealthLog
{
    public long HorseHealthLogId { get; set; }

    public long TransportId { get; set; }

    public long HorseId { get; set; }

    public long EmployeeId { get; set; }

    public DateTime CheckedAt { get; set; }

    public decimal? TemperatureCelsius { get; set; }

    public int? HeartRateBpm { get; set; }

    public int? RespiratoryRateBpm { get; set; }

    public string? BehaviorNote { get; set; }

    public string? SymptomNote { get; set; }

    public string? InjuryNote { get; set; }

    public string? FeedingNote { get; set; }

    public string? HydrationNote { get; set; }

    public string? TreatmentProvided { get; set; }

    public string? MedicationGiven { get; set; }

    public bool VeterinarianRequired { get; set; }

    public string? VeterinarianNote { get; set; }

    public string? LocationDescription { get; set; }

    public string? PhotoUrl { get; set; }

    public string? GeneralNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual Horse Horse { get; set; } = null!;

    public virtual Transport Transport { get; set; } = null!;
}
