using System.Text.Json.Serialization;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.IncidentService;

public static class Response
{
    public sealed record Hold(string Status, DateTimeOffset StartedAt, DateTimeOffset HoldUntil, int HoldMinutes,
        int MonitorIntervalMinutes, string Reason, LocationBrief? SafeStopLocation, Reporter DecidedBy);
    public sealed record HoldResult(Guid IncidentReportId, string IncidentStatus, Hold Hold, IReadOnlyList<Reporter> Notified);
    public sealed record EscalationResult(Guid IncidentReportId, string IncidentStatus,
        IReadOnlyList<Reporter> NotifiedLogisticsManagers, DateTimeOffset SentAt);
    public sealed record RerouteIncident(Guid IncidentReportId, string IncidentCode, string Category, string Severity,
        string Status, string Title, bool? VehicleOperable, DateTimeOffset OccurredAt);
    public sealed record RerouteTransport(Guid TransportId, string TransportCode, string Status, Guid? TransportPlanId);
    public sealed record RerouteSegment(Guid RoutePlanRouteId, Guid RoutePlanId, int SequenceNo, string RouteType,
        LocationBrief? FromLocation, LocationBrief? ToLocation, string Status, DateTimeOffset? PlannedArrivalAt,
        DateTimeOffset? ActualDepartureAt, DateTimeOffset? ActualArrivalAt);
    public sealed record ReroutePlan(Guid RoutePlanId, string RoutePlanCode, string Status, decimal? TotalDistanceKm,
        int? EstimatedDurationMinutes, DateTimeOffset? PlannedArrivalAt, decimal? EtaDelayMinutes, bool IsAvailable,
        string? UnavailableReason, IReadOnlyList<RouteRestRules.Check> Checks,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BranchSequenceNo = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LocationBrief? BranchLocation = null);
    public sealed record RerouteDossier(Guid ComplianceDossierId, string DossierCode, string DossierType, string Status);
    public sealed record RerouteContext(DateTimeOffset ServerTime, RerouteIncident Incident, RerouteTransport Transport,
        RerouteSegment? CurrentSegment, IReadOnlyList<RerouteSegment> RemainingSegments, IReadOnlyList<ReroutePlan> BackupPlans,
        ReroutePlan? ReturnPlan, IReadOnlyList<RerouteDossier> ComplianceDossiers, Hold? Hold, IReadOnlyList<string> AvailableActions);
    public sealed record SeverityResult(Incident Value, string PreviousSeverity);
    public sealed record Counts(int Reported, int Pending, int Resolved, int UrgentOpen);
    public sealed record ListResult(DateTimeOffset ServerTime, Counts Counts, IReadOnlyList<ListItem> Items,
        int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record Reporter(Guid EmployeeId, string EmployeeCode, string FullName, string Role);
    public sealed record ListVehicle(Guid VehicleId, string VehicleCode, string? LicensePlate);
    public sealed record ListHorse(Guid HorseId, string HorseName);
    public sealed record ListItem(Guid IncidentReportId, string IncidentCode, TransportBrief Transport, string Category,
        string Severity, string Status, string Title, DateTimeOffset OccurredAt, DateTimeOffset ReportedAt,
        LocationBrief? Location, Reporter ReportedBy, ListVehicle? Vehicle, IReadOnlyList<ListHorse> Horses,
        bool EmergencySettlementRequired, int CostRequestCount, int PendingCostCount, long ApprovedTotal, DateTimeOffset UpdatedAt);
    public sealed record CreateResult(Incident Value, bool Created);
    public sealed record EmergencyContacts(DateTimeOffset ServerTime, Guid TransportId, IReadOnlyList<EmployeeBrief> Contacts);
    public sealed record EmployeeBrief(Guid EmployeeId, string EmployeeCode, string FullName, string Role, string? Phone);
    public sealed record TransportBrief(Guid TransportId, string TransportCode, string Status, string BookingCode);
    public sealed record VehicleBrief(Guid VehicleId, string VehicleCode, string? LicensePlate, string? VehicleName, string Status);
    public sealed record HorseBrief(Guid HorseId, string HorseName, string? MicrochipNumber, string? Breed, int? StallNo);
    public sealed record Photo(Guid PhotoId, string PhotoUrl, string? Caption, Guid? HorseHealthIncidentDetailId, int SortOrder);
    public sealed record Effects(object NotifiedPriority, bool EmailQueued,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? VehicleStatusChanged = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanAddHorseDetails = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HealthLogsCreated = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? FirstAidGuide = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CustomerNotified = null);
    public sealed record Safety(string Message, IReadOnlyList<EmployeeBrief> EmergencyContacts);
    public sealed record CostSummary(long RequestedTotal, long ApprovedTotal, int PendingCount, string Currency = Vnd.Currency);
    public sealed record RouteChange(Guid RoutePlanId, string RoutePlanCode, string RouteType, string Status, DateTimeOffset UpdatedAt);

    public sealed record Incident
    {
        public Guid IncidentReportId { get; init; }
        public string IncidentCode { get; init; } = string.Empty;
        public TransportBrief? Transport { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Severity { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? ImmediateAction { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public DateTimeOffset ReportedAt { get; init; }
        public DateTimeOffset? ResolvedAt { get; init; }
        public string? ResolutionSummary { get; init; }
        public DateTimeOffset? CancelledAt { get; init; }
        public string? CancellationReason { get; init; }
        public LocationBrief? Location { get; init; }
        public required EmployeeBrief ReportedBy { get; init; }
        public VehicleBrief? Vehicle { get; init; }
        public IReadOnlyList<HorseBrief> Horses { get; init; } = [];
        public VehicleDetail? VehicleDetail { get; init; }
        public IReadOnlyList<HorseDetail> HorseDetails { get; init; } = [];
        public IReadOnlyList<Photo> Photos { get; init; } = [];
        public int PhotoCount => Photos.Count;
        public bool EmergencySettlementRequired { get; init; }
        public IReadOnlyList<CostRequest> CostRequests { get; init; } = [];
        public int CostRequestCount => CostRequests.Count;
        public int PendingCostCount => CostSummary.PendingCount;
        public long ApprovedTotal => CostSummary.ApprovedTotal;
        public CostSummary CostSummary { get; init; } = new(0, 0, 0);
        public IReadOnlyList<RouteChange> RouteChanges { get; init; } = [];
        public Hold? Hold { get; init; }
        public bool CanResolve { get; init; }
        public IReadOnlyList<string> AvailableActions { get; init; } = [];
        public DateTimeOffset ServerTime { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Effects? Effects { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Safety? Safety { get; init; }
    }

    public sealed record VehicleDetail(Guid VehicleIncidentDetailId, VehicleBrief Vehicle, string IncidentType,
        string? VehicleCondition, string? DamageDescription, bool VehicleOperable, bool RepairRequired, bool TowingRequired,
        bool ReplacementVehicleRequired, long? EstimatedRepairCost, decimal? EstimatedRepairHours, string? DriverSafetyStatus,
        string? CargoConditionNote, string? RepairNote, DateTimeOffset UpdatedAt);

    public sealed record HorseDetail(Guid HorseHealthIncidentDetailId, HorseBrief Horse, string IncidentType, string ConditionDescription,
        string? Symptoms, decimal? BodyTemperatureCelsius, int? HeartRateBpm, int? RespiratoryRateBpm, string? FirstAidProvided,
        bool VeterinarianRequired, DateTimeOffset? VeterinarianContactedAt, bool IsolationRequired, string? VeterinarianInstruction,
        string? MedicationGiven, string? OutcomeNote, bool? HorseTransportable, DateTimeOffset UpdatedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? VitalWarnings = null);

    public sealed record CostDecision(string Decision, long? ApprovedAmount, string? DecisionReason, EmployeeBrief DecidedBy, DateTimeOffset DecidedAt);
    public sealed record CostRequest(Guid EmergencyCostRequestId, string RequestCode, string Category, string Description,
        string Justification, long RequestedAmount, long? ApprovedAmount, string Currency, string Status, DateTimeOffset RequestedAt,
        EmployeeBrief RequestedBy, CostDecision? Decision, DateTimeOffset? CancelledAt, string? CancellationReason,
        bool InvoiceRequired, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CostIncident? Incident { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TransportCode { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? AvailableActions { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<Photo>? Photos { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CostIncidentSummary? IncidentSummary { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<OtherCostRequest>? OtherRequests { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TransportService.Response.HorseHealthSummary? HorseHealthSummary { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public CostRoute? CurrentRoute { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? ServerTime { get; init; }
    }

    public sealed record CostIncident(Guid IncidentReportId, string IncidentCode, string Category, string Severity, string Status, Guid? TransportId);
    public sealed record CostIncidentSummary(Guid IncidentReportId, string IncidentCode, string Category, string Severity,
        string Status, string Title, DateTimeOffset OccurredAt, LocationBrief? Location, VehicleBrief? Vehicle);
    public sealed record OtherCostRequest(Guid EmergencyCostRequestId, string RequestCode, string Category, long RequestedAmount, long? ApprovedAmount, string Status);
    public sealed record CostRoute(Guid RoutePlanId, string RoutePlanCode, string RouteType, string Status);
    public sealed record CostCounts(int Pending, int Approved, int Rejected, int Cancelled);
    public sealed record CostListResult(DateTimeOffset ServerTime, CostCounts Counts, IReadOnlyList<CostRequest> Items,
        int PageIndex, int PageSize, int TotalItems, int TotalPages);
}
