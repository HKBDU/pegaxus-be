namespace Pegaxus.Service.HorseHealthLogService;

public static class Response
{
    public sealed record HorseBrief(Guid HorseId, string HorseName, string? MicrochipNumber, string? PassportNumber);
    public sealed record HorseSummary(Guid HorseId, string HorseName, string? MicrochipNumber);
    public sealed record CheckedBy(Guid EmployeeId, string EmployeeCode, string FullName);
    public sealed record Effects(bool EmergencyNotified, bool CanReportIncident);
    public sealed record Photo(string PhotoUrl, decimal? Latitude = null, decimal? Longitude = null, DateTimeOffset? CapturedAt = null, string? Caption = null);
    public sealed record CreateResult(HealthLog Value, bool Created);
    public sealed record BatchResult(BatchResponse Value, bool Created);
    public sealed record Summary(Guid HorseHealthLogId, HorseSummary Horse, DateTimeOffset CheckedAt, string LogType,
        string HealthStatus, string? HydrationStatus, CheckedBy CheckedBy, bool VeterinarianRequired, bool HasPhoto);
    public sealed record BatchResponse(Guid TransportId, string LogType, int CreatedCount, IReadOnlyList<Summary> Logs);

    public sealed record HealthLog
    {
        public Guid HorseHealthLogId { get; init; }
        public Guid TransportId { get; init; }
        public required HorseBrief Horse { get; init; }
        public DateTimeOffset CheckedAt { get; init; }
        public required string LogType { get; init; }
        public required string HealthStatus { get; init; }
        public string? HydrationStatus { get; init; }
        public required CheckedBy CheckedBy { get; init; }
        public decimal? TemperatureCelsius { get; init; }
        public int? HeartRateBpm { get; init; }
        public int? RespiratoryRateBpm { get; init; }
        public string? BehaviorNote { get; init; }
        public string? SymptomNote { get; init; }
        public string? InjuryNote { get; init; }
        public string? FeedingNote { get; init; }
        public string? HydrationNote { get; init; }
        public string? TreatmentProvided { get; init; }
        public string? MedicationGiven { get; init; }
        public bool VeterinarianRequired { get; init; }
        public string? VeterinarianNote { get; init; }
        public string? LocationDescription { get; init; }
        // ponytail: GPS and photo metadata remain null until B-04/B-05 provide storage columns.
        public object? GpsCoordinates { get; init; }
        public IReadOnlyList<Photo> Photos { get; init; } = [];
        public string? GeneralNote { get; init; }
        public bool HasPhoto { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public Effects Effects { get; init; } = new(false, false);
    }
}
