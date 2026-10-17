namespace Pegaxus.Service.HorseHealthService;

public static class Response
{
    public sealed record HorseBrief(Guid HorseId, string HorseName, string? MicrochipNumber, string? PassportNumber);
    public sealed record HorseSummary(Guid HorseId, string HorseName, string? MicrochipNumber);
    public sealed record CheckedBy(Guid EmployeeId, string EmployeeCode, string FullName);
    public sealed record Escort(Guid EmployeeId, string EmployeeCode, string FullName, string Role, string? Phone);
    public sealed record LatestLog(Guid HorseHealthLogId, string LogType, string HealthStatus, string? HydrationStatus,
        DateTimeOffset CheckedAt, CheckedBy CheckedBy, string? LocationDescription, string? LatestNote, bool VeterinarianRequired);
    public sealed record HorseOverview(HorseBrief Horse, string? LatestHealthStatus, LatestLog? LatestLog, int LogCount);
    public sealed record OverviewResponse(DateTimeOffset ServerTime, Guid TransportId, string TransportStatus,
        Escort? CurrentEscort, IReadOnlyList<HorseOverview> Horses);
    public sealed record ListItem(Guid HorseHealthLogId, HorseSummary Horse, DateTimeOffset CheckedAt, string LogType,
        string HealthStatus, string? HydrationStatus, CheckedBy CheckedBy, bool VeterinarianRequired, bool HasPhoto);
    public sealed record ListResponse(DateTimeOffset ServerTime, HorseBrief Horse, IReadOnlyList<ListItem> Items,
        int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record Photo(string PhotoUrl, decimal? Latitude = null, decimal? Longitude = null,
        DateTimeOffset? CapturedAt = null, string? Caption = null);

    public sealed record DetailResponse
    {
        public DateTimeOffset ServerTime { get; init; }
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
        // shortcut: GPS is unavailable until B-05 supplies health-log coordinates.
        public object? GpsCoordinates { get; init; }
        public IReadOnlyList<Photo> Photos { get; init; } = [];
        public string? GeneralNote { get; init; }
        public bool HasPhoto { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
