namespace Pegaxus.Service.RouteChangeService;

public static class Response
{
    public sealed record Created(Guid RoutePlanId, string RouteType, string Status, DateTimeOffset RequestedAt);
    public sealed record DossierBrief(Guid ComplianceDossierId, string DossierCode, string DossierType, string Status,
        int? InheritedRequirementCount);
    public sealed record DecisionResult(Guid RoutePlanId, string RouteType, string Decision, string Status,
        DossierBrief? ComplianceDossier, int? BranchSequenceNo, LocationBrief? BranchLocation, DateTimeOffset UpdatedAt);
    public sealed record ActivationResult(Guid RoutePlanId, string RouteType, string Status,
        IReadOnlyList<RoutePlanService.Response.SegmentResponse> Segments, DateTimeOffset UpdatedAt);
    public sealed record DriverNoticeResult(Guid RoutePlanId, IReadOnlyList<EmployeeBrief> NotifiedDrivers, DateTimeOffset SentAt);
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
}
