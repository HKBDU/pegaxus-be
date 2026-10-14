using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// HorseHealthLog: Nhật ký kiểm tra sức khỏe của Horse trong Transport do Employee ghi nhận, 
/// gồm chỉ số sinh tồn, tình trạng, chăm sóc và ghi chú hiện trường.
/// </summary>
public partial class HorseHealthLog
{
    public Guid HorseHealthLogId { get; set; }

    public Guid TransportId { get; set; }

    public Guid HorseId { get; set; }

    public Guid EmployeeId { get; set; }

    public HorseHealthLogType LogType { get; set; }

    public DateTime CheckedAt { get; set; }

    public HorseHealthStatus HealthStatus { get; set; }

    public HydrationStatus? HydrationStatus { get; set; }

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
