using System;
using System.Collections.Generic;

namespace Pegaxus.Repository.Entities;

public partial class HorseHealthIncidentDetail
{
    public long HorseHealthIncidentDetailId { get; set; }

    public long IncidentReportId { get; set; }

    public long HorseId { get; set; }

    public string ConditionDescription { get; set; } = null!;

    public string? Symptoms { get; set; }

    public decimal? BodyTemperatureCelsius { get; set; }

    public int? HeartRateBpm { get; set; }

    public int? RespiratoryRateBpm { get; set; }

    public string? FirstAidProvided { get; set; }

    public bool VeterinarianRequired { get; set; }

    public DateTime? VeterinarianContactedAt { get; set; }

    public string? VeterinarianInstruction { get; set; }

    public string? MedicationGiven { get; set; }

    public bool? HorseTransportable { get; set; }

    public string? OutcomeNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Horse Horse { get; set; } = null!;

    public virtual IncidentReport IncidentReport { get; set; } = null!;
}
