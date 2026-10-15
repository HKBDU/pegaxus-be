namespace Pegaxus.Service.TransportPlanService;

public static class Request
{
    public sealed class CreateRequest
    {
        public Guid? OriginLocationId { get; set; }
        public Guid? DestinationLocationId { get; set; }
        public DateTimeOffset PlannedStartAt { get; set; }
        public DateTimeOffset PlannedEndAt { get; set; }
        public decimal? EstimatedDistanceKm { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? PlanningNote { get; set; }
    }

    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; }
        public string? Search { get; set; }
        public string? RoutePlanStatus { get; set; }
        public string? SortBy { get; set; }
    }

    public sealed class UpdateRequest
    {
        public Guid? OriginLocationId { get; set; }
        public Guid? DestinationLocationId { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string? UpdateReason { get; set; }
        public DateTimeOffset? PlannedStartAt { get; set; }
        public DateTimeOffset? PlannedEndAt { get; set; }
        public decimal? EstimatedDistanceKm { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? PlanningNote { get; set; }
    }

    public sealed class Phase1AssignmentRequest
    {
        public Guid TransportSpecialistId { get; set; }
        public Guid RouteCoordinatorId { get; set; }
    }

    public sealed class UpdatePhase1AssignmentRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public Guid? TransportSpecialistId { get; set; }
        public Guid? RouteCoordinatorId { get; set; }
        public required string Reason { get; set; }
    }
}
