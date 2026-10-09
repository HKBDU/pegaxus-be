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
}
