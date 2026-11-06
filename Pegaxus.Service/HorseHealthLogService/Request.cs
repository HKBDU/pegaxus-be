using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.HorseHealthLogService;

public static class Request
{
    public class RecordRequest
    {
        public string LogType { get; set; } = string.Empty;
        public DateTimeOffset? CheckedAt { get; set; }
        public string? LocationDescription { get; set; }
        public string? GeneralNote { get; set; }
        public List<UploadService.Request.UploadedAssetReference> Files { get; set; } = [];
    }

    public sealed class AddPhotosRequest
    {
        public List<UploadService.Request.UploadedAssetReference> Files { get; set; } = [];
        // ponytail: photo metadata is ignored; add a child table when per-photo metadata is approved.
    }

    public sealed class CreateRequest : RecordRequest
    {
        public Guid HorseId { get; set; }
        public string HealthStatus { get; set; } = string.Empty;
        public string HydrationStatus { get; set; } = string.Empty;
        public decimal? TemperatureCelsius { get; set; }
        public int? HeartRateBpm { get; set; }
        public int? RespiratoryRateBpm { get; set; }
        public string? BehaviorNote { get; set; }
        public string? FeedingNote { get; set; }
        public string? HydrationNote { get; set; }
        public string? SymptomNote { get; set; }
        public string? InjuryNote { get; set; }
        public string? TreatmentProvided { get; set; }
        public string? MedicationGiven { get; set; }
        public bool VeterinarianRequired { get; set; }
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? VeterinarianNote { get; set; }
    }
}
