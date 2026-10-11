using System.Text.Json.Serialization;

namespace Pegaxus.Service.VehicleService;

public static class Response
{
    public sealed record ListResponse(IReadOnlyList<VehicleResponse> Items, int PageIndex, int PageSize, int TotalItems, int TotalPages);
    public sealed class VehicleResponse
    {
        public Guid VehicleId { get; init; }
        public string VehicleCode { get; init; } = "";
        public string? VehicleName { get; init; }
        public string TransportMode { get; init; } = "ROAD";
        public string? LicensePlate { get; init; }
        public string? VehicleType { get; init; }
        public string? Manufacturer { get; init; }
        public string? Model { get; init; }
        public int? CapacityHorses { get; init; }
        public string Status { get; init; } = "";
        public string? RegistrationNumber { get; init; }
        public DateOnly? RegistrationExpiryDate { get; init; }
        public string? RegistrationStatus { get; init; }
        public string? InsuranceNumber { get; init; }
        public DateOnly? InsuranceExpiryDate { get; init; }
        public string? InsuranceStatus { get; init; }
        public string? SpecialEquipment { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = [];
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsAvailable { get; init; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<AssignmentResponse>? UpcomingAssignments { get; set; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
    public sealed record AssignmentResponse(Guid RoutePlanRouteId, Guid RoutePlanId, string RoutePlanCode,
        Guid TransportPlanId, string PlanCode, int SequenceNo, string FromLocationName, string ToLocationName,
        DateTimeOffset? PlannedDepartureAt, DateTimeOffset? PlannedArrivalAt);
    public sealed record UpdateResponse(Guid VehicleId, string? RegistrationStatus, string? InsuranceStatus,
        IReadOnlyList<string> Warnings, DateTimeOffset UpdatedAt);
    public sealed record StatusResponse(Guid VehicleId, string PreviousStatus, string Status, DateTimeOffset UpdatedAt);
}
