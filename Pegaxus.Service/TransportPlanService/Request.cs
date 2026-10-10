namespace Pegaxus.Service.TransportPlanService;

public static class Request
{
    public sealed class CreateRequest
    {
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
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string? UpdateReason { get; set; }
        public DateTimeOffset? PlannedStartAt { get; set; }
        public DateTimeOffset? PlannedEndAt { get; set; }
        public decimal? EstimatedDistanceKm { get; set; }
        public int? EstimatedDurationMinutes { get; set; }
        public string? PlanningNote { get; set; }
    }
}
