using System.Collections.Generic;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Repository.Entities;

/// <summary>
/// HorseHealthIncidentDetail: Chi tiết sức khỏe của một Horse trong IncidentReport, 
/// gồm triệu chứng, chỉ số sinh tồn, sơ cứu, yêu cầu bác sĩ thú y và kết quả xử lý.
/// </summary>
public partial class HorseHealthIncidentDetail
{
    public Guid HorseHealthIncidentDetailId { get; set; }

    public Guid IncidentReportId { get; set; }

    public Guid HorseId { get; set; }

    public HorseIncidentType IncidentType { get; set; }

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

    public bool IsolationRequired { get; set; }

    public string? OutcomeNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Horse Horse { get; set; } = null!;

    public virtual IncidentReport IncidentReport { get; set; } = null!;

    public virtual ICollection<IncidentPhoto> IncidentPhotos { get; set; } = new List<IncidentPhoto>();
}
