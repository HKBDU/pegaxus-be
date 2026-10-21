namespace Pegaxus.Service.IncidentService;

public static class Request
{
    public class VehicleFields
    {
        public bool? VehicleOperable { get; set; }
        public bool? RepairRequired { get; set; }
        public bool? TowingRequired { get; set; }
        public bool? ReplacementVehicleRequired { get; set; }
        public string? VehicleCondition { get; set; }
        public string? DamageDescription { get; set; }
        public decimal? EstimatedRepairCost { get; set; }
        public decimal? EstimatedRepairHours { get; set; }
        public string? DriverSafetyStatus { get; set; }
        public string? CargoConditionNote { get; set; }
        public List<UploadService.Request.UploadedAssetReference> PhotoFiles { get; set; } = [];
    }

    public sealed class ReportVehicleRequest : VehicleFields
    {
        public Guid TransportId { get; set; }
        public Guid VehicleId { get; set; }
        public string Category { get; set; } = "VEHICLE";
        public string IncidentType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ImmediateAction { get; set; }
        public bool EmergencySettlementRequired { get; set; }
        public DateTimeOffset? OccurredAt { get; set; }
        public Guid? LocationId { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationDescription { get; set; }
        public string? Country { get; set; }
    }

    public sealed class UpdateVehicleRequest : VehicleFields
    {
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? RepairNote { get; set; }
    }
}
