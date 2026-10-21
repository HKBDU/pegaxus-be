using System.Text.Json;
using System.Text.Json.Serialization;

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

    public class AddHorseDetailsRequest
    {
        public List<HorseDetail> Horses { get; set; } = [];
        public List<UploadService.Request.UploadedAssetReference> PhotoFiles { get; set; } = [];
    }

    public sealed class ReportHorseHealthRequest : AddHorseDetailsRequest
    {
        public Guid TransportId { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? ImmediateAction { get; set; }
        public bool EmergencySettlementRequired { get; set; }
        public DateTimeOffset? OccurredAt { get; set; }
        public Guid? LocationId { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationDescription { get; set; }
        public string? Country { get; set; }
    }

    public sealed class HorseDetail
    {
        public Guid HorseId { get; set; }
        public string IncidentType { get; set; } = string.Empty;
        public string ConditionDescription { get; set; } = string.Empty;
        public string? Symptoms { get; set; }
        public decimal? BodyTemperatureCelsius { get; set; }
        public int? HeartRateBpm { get; set; }
        public int? RespiratoryRateBpm { get; set; }
        public string? FirstAidProvided { get; set; }
        public bool VeterinarianRequired { get; set; }
        public bool IsolationRequired { get; set; }
        public DateTimeOffset? VeterinarianContactedAt { get; set; }
        public string? VeterinarianInstruction { get; set; }
        public string? MedicationGiven { get; set; }
        public bool? HorseTransportable { get; set; }
    }

    public sealed class FollowUpHorseRequest
    {
        private bool? horseTransportable;
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public string? OutcomeNote { get; set; }
        public string? MedicationGiven { get; set; }
        public string? VeterinarianInstruction { get; set; }
        public DateTimeOffset? VeterinarianContactedAt { get; set; }
        public bool? HorseTransportable
        {
            get => horseTransportable;
            set { horseTransportable = value; HasHorseTransportable = true; }
        }
        [JsonIgnore]
        public bool HasHorseTransportable { get; private set; }
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraFields { get; set; }
    }
}
