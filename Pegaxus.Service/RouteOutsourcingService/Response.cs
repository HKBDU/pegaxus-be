namespace Pegaxus.Service.RouteOutsourcingService;

public sealed class Response
{
    public Guid RoutePlanId { get; init; }
    public required string RoutePlanStatus { get; init; }
    public required SegmentResponse Segment { get; init; }
    public bool ClearedPersonnel { get; init; }
    public bool ClearedVehicles { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public sealed class SegmentResponse
    {
        public Guid RoutePlanRouteId { get; init; }
        public int SequenceNo { get; init; }
        public bool IsOutsourced { get; init; }
        public required IReadOnlyList<VehicleResponse> Vehicles { get; init; }
        public OutsourcingResponse? Outsourcing { get; init; }
        public EmployeeResponse? Driver { get; init; }
        public EmployeeResponse? Escort { get; init; }
        public string? Note { get; init; }
    }

    public sealed class OutsourcingResponse
    {
        public required ProviderResponse Provider { get; init; }
    }

    public sealed class ProviderResponse
    {
        public Guid TransportProviderId { get; init; }
        public required string ProviderCode { get; init; }
        public required string ProviderName { get; init; }
        public required string Status { get; init; }
        public required string[] SupportedModes { get; init; }
    }

    public sealed class EmployeeResponse
    {
        public Guid EmployeeId { get; init; }
        public required string EmployeeCode { get; init; }
        public required string FullName { get; init; }
        public required string Role { get; init; }
    }

    public sealed class VehicleResponse
    {
        public Guid VehicleId { get; init; }
        public required string VehicleCode { get; init; }
        public string? VehicleName { get; init; }
        public string? LicensePlate { get; init; }
        public required string TransportMode { get; init; }
        public int? CapacityHorses { get; init; }
    }
}
