namespace Pegaxus.Service.TransportPlanService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<ListItemResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class ListItemResponse
    {
        public Guid TransportPlanId { get; init; }
        public required string PlanCode { get; init; }
        public required string Status { get; init; }
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string CustomerName { get; init; }
        public DateTimeOffset? PlannedStartAt { get; init; }
        public DateTimeOffset? PlannedEndAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class CreateResponse
    {
        public Guid TransportPlanId { get; init; }
        public required string PlanCode { get; init; }
        public required string Status { get; init; }
        public required BookingResponse Booking { get; init; }
        public required TransportResponse Transport { get; init; }
        public DateTimeOffset PlannedStartAt { get; init; }
        public DateTimeOffset PlannedEndAt { get; init; }
        public string? PlanningNote { get; init; }
        public decimal? EstimatedDistanceKm { get; init; }
        public int? EstimatedDurationMinutes { get; init; }
        public required IReadOnlyList<global::Pegaxus.Service.BookingService.Response.BookingAssignmentResponse> AssignedEmployees { get; init; }
        public ApprovedByResponse? ApprovedBy { get; init; }
        public DateTimeOffset? ApprovedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class BookingResponse
    {
        public Guid BookingId { get; init; }
        public required string BookingCode { get; init; }
        public required string Status { get; init; }
        public bool IsInternational { get; init; }
        public required string CustomerName { get; init; }
        public required string OriginAddress { get; init; }
        public required string OriginCountry { get; init; }
        public required string DestinationAddress { get; init; }
        public required string DestinationCountry { get; init; }
        public int TotalHorses { get; init; }
        public DateOnly RequestedDepartureDate { get; init; }
        public DateOnly? RequestedArrivalDate { get; init; }
        public string? SpecialRequirements { get; init; }
    }

    public sealed class TransportResponse
    {
        public Guid TransportId { get; init; }
        public required string TransportCode { get; init; }
        public required string Status { get; init; }
    }

    public sealed class ApprovedByResponse
    {
        public Guid EmployeeId { get; init; }
        public required string FullName { get; init; }
    }

    public sealed class Phase1AssignmentResponse
    {
        public Guid TransportPlanId { get; init; }
        public required IReadOnlyList<Phase1AssignedEmployeeResponse> AssignedEmployees { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class Phase1AssignedEmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
        public required string Responsibility { get; init; }
        public bool IsPrimary { get; init; }
        public DateTimeOffset AssignedAt { get; init; }
    }

    public sealed class AssignmentsResponse
    {
        public Guid TransportPlanId { get; init; }
        public required Phase1AssignmentsResponse Phase1 { get; init; }
        public required Phase2AssignmentsResponse Phase2 { get; init; }
    }

    public sealed class Phase1AssignmentsResponse
    {
        public bool IsAssigned { get; init; }
        public bool IsLocked { get; init; }
        public AssignmentEmployeeResponse? TransportSpecialist { get; init; }
        public AssignmentEmployeeResponse? RouteCoordinator { get; init; }
    }

    public sealed class Phase2AssignmentsResponse
    {
        public bool Enabled { get; init; }
        public Guid? RoutePlanId { get; init; }
        public bool IsPersonnelComplete { get; init; }
        public required IReadOnlyList<AssignmentSegmentResponse> Segments { get; init; }
    }

    public sealed class AssignmentSegmentResponse
    {
        public Guid RoutePlanRouteId { get; init; }
        public int SequenceNo { get; init; }
        public bool IsOutsourced { get; init; }
        public DateTimeOffset? PlannedDepartureAt { get; init; }
        public DateTimeOffset? PlannedArrivalAt { get; init; }
        public AssignmentEmployeeResponse? Driver { get; init; }
        public AssignmentEmployeeResponse? Escort { get; init; }
        public AssignmentProviderResponse? Provider { get; init; }
        public bool IsComplete { get; init; }
    }

    public sealed class AssignmentEmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
    }

    public sealed class AssignmentProviderResponse
    {
        public Guid TransportProviderId { get; init; }
        public required string ProviderCode { get; init; }
        public required string ProviderName { get; init; }
    }
}
