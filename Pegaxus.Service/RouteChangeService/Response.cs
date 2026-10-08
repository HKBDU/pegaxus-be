namespace Pegaxus.Service.RouteChangeService;

public static class Response
{
    public sealed record Created(Guid ChangeRequestId, Guid RoutePlanId, string Status, bool RequiresApproval,
        string[] ApprovalReasons, DateTimeOffset CreatedAt);
    public sealed record ListResponse(List<ListItem> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record ListItem(Guid ChangeRequestId, Guid RoutePlanId, Guid TransportPlanId, string PlanCode,
        string BookingCode, string Status, string Reason, string[] ApprovalReasons, DateTimeOffset NewEstimatedArrivalAt, DateTimeOffset CreatedAt);
    public sealed record Detail(Guid ChangeRequestId, Guid RoutePlanId, Guid TransportPlanId, string Status,
        string Reason, EmployeeBrief RequestedBy, List<Segment> CurrentSegments, List<Segment> ProposedSegments,
        Guid? BackupRoutePlanId, Comparison Comparison, string[] ApprovalReasons, decimal? EstimatedExtraCost,
        string? Currency, string? Note, DecisionInfo? Decision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    public sealed record EmployeeBrief(Guid EmployeeId, string EmployeeCode, string FullName, string Role);
    public sealed record LocationBrief(Guid LocationId, string LocationName, string? LocationType, string? City, string Country,
        decimal? Latitude, decimal? Longitude);
    public sealed record RouteBrief(Guid RouteId, string RouteCode, string? RouteName, string TransportMode,
        decimal? DistanceKm, int? EstimatedDurationMinutes);
    public sealed record VehicleBrief(Guid VehicleId, string VehicleCode, string? VehicleName, string? LicensePlate,
        string TransportMode, int? CapacityHorses);
    public sealed record ProviderBrief(Guid TransportProviderId, string ProviderCode, string ProviderName, string Status,
        string[] SupportedModes);
    public sealed record Outsourcing(ProviderBrief Provider, string? ServiceType, string? ProviderReference);
    public sealed record Segment(Guid RoutePlanRouteId, int SequenceNo, string RouteType, RouteBrief Route,
        LocationBrief FromLocation, LocationBrief ToLocation, DateTimeOffset PlannedDepartureAt, DateTimeOffset PlannedArrivalAt,
        DateTimeOffset? ActualDepartureAt, DateTimeOffset? ActualArrivalAt, string Status, decimal? DwellAfterMinutes,
        string ExecutionType, List<VehicleBrief> Vehicles, Outsourcing? Outsourcing, EmployeeBrief? Driver, EmployeeBrief? Escort, string? Note);
    public sealed record Comparison(decimal DistanceDeltaKm, decimal DelayMinutes, bool BcpChanged, bool Additional24hRest,
        LocationBrief? PreviousBcp, LocationBrief? NewBcp, DateTimeOffset PreviousEta, DateTimeOffset NewEta);
    public sealed record DecisionInfo(string Decision, Guid EmployeeId, string? Note, DateTimeOffset DecidedAt);
    public sealed record DecisionResult(Guid ChangeRequestId, string Status, Guid RoutePlanId, DateTimeOffset NewEstimatedArrivalAt,
        Guid[] SupersededSegmentIds, Guid[] NewSegmentIds, Guid[] ReturnSegmentIds, Guid[] CancelledSegmentIds,
        string[] Notified, bool ExtraCostRecordedForIncident, bool IncidentSettlementTriggered, DateTimeOffset UpdatedAt);
}
