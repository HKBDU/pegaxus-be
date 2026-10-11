namespace Pegaxus.Service.PersonnelAssignmentService;

public static class Response
{
    public sealed record EmployeeBrief(Guid EmployeeId, string EmployeeCode, string FullName, string Role);
    public sealed record ProviderBrief(Guid TransportProviderId, string ProviderCode, string ProviderName);
    public sealed record SegmentResponse(Guid RoutePlanRouteId, int SequenceNo, bool IsOutsourced,
        DateTimeOffset? PlannedDepartureAt, DateTimeOffset? PlannedArrivalAt,
        EmployeeBrief? Driver, EmployeeBrief? Escort, ProviderBrief? Provider, bool IsComplete);
    public sealed record Phase1Response(bool IsAssigned, bool IsLocked, EmployeeBrief? TransportSpecialist, EmployeeBrief? RouteCoordinator);
    public sealed record Phase2Response(bool Enabled, Guid? RoutePlanId, bool IsPersonnelComplete, IReadOnlyList<SegmentResponse> Segments);
    public sealed record AssignmentsResponse(Guid TransportPlanId, Phase1Response Phase1, Phase2Response Phase2, DateTimeOffset? UpdatedAt);
    public sealed record AssignResponse(Guid RoutePlanId, bool IsPersonnelComplete, IReadOnlyList<SegmentResponse> Segments, DateTimeOffset UpdatedAt);
    public sealed record AssignableResponse(IReadOnlyList<EmployeeBrief> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed record ReplaceResponse(Guid RoutePlanId, SegmentResponse Segment, bool IsPersonnelComplete, DateTimeOffset UpdatedAt);
}
