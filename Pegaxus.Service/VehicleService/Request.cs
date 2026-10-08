using System.Text.Json.Serialization;

namespace Pegaxus.Service.VehicleService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? TransportMode { get; set; }
        public string? Status { get; set; }
        public string? VehicleType { get; set; }
        public int? MinCapacity { get; set; }
        public DateOnly? AvailableFrom { get; set; }
        public DateOnly? AvailableTo { get; set; }
        public string? Search { get; set; }
        public string SortBy { get; set; } = "vehicleCode:asc";
    }

    public class VehicleFields
    {
        public string? VehicleName { get; set; }
        public string TransportMode { get; set; } = "ROAD";
        public string LicensePlate { get; set; } = "";
        public string? VehicleType { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public int? CapacityHorses { get; set; }
        public string? RegistrationNumber { get; set; }
        public DateOnly? RegistrationExpiryDate { get; set; }
        public string? InsuranceNumber { get; set; }
        public DateOnly? InsuranceExpiryDate { get; set; }
        public string? SpecialEquipment { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class CreateRequest : VehicleFields
    {
        public string VehicleCode { get; set; } = "";
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class UpdateRequest : VehicleFields
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public Guid? VehicleId { get; set; }
        public string? VehicleCode { get; set; }
        public string? Status { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class StatusRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public string Status { get; set; } = "";
        public string Reason { get; set; } = "";
    }
}
