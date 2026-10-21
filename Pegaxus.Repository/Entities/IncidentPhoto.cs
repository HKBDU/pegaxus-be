namespace Pegaxus.Repository.Entities;

public partial class IncidentPhoto
{
    public Guid IncidentPhotoId { get; set; }

    public Guid IncidentReportId { get; set; }

    public Guid? VehicleIncidentDetailId { get; set; }

    public Guid? HorseHealthIncidentDetailId { get; set; }

    public Guid? EmergencyCostRequestId { get; set; }

    public string PhotoUrl { get; set; } = null!;

    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual IncidentReport IncidentReport { get; set; } = null!;

    public virtual VehicleIncidentDetail? VehicleIncidentDetail { get; set; }

    public virtual HorseHealthIncidentDetail? HorseHealthIncidentDetail { get; set; }

    public virtual EmergencyCostRequest? EmergencyCostRequest { get; set; }
}
