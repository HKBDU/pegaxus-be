namespace Pegaxus.Service.RouteService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<DetailResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class DetailResponse
    {
        public Guid RouteId { get; init; }
        public required string RouteCode { get; init; }
        public string? RouteName { get; init; }
        public required string TransportMode { get; init; }
        public required LocationResponse FromLocation { get; init; }
        public required LocationResponse ToLocation { get; init; }
        public decimal? DistanceKm { get; init; }
        public int? EstimatedDurationMinutes { get; init; }
        public string? RouteNote { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class LocationResponse
    {
        public Guid LocationId { get; init; }
        public required string LocationName { get; init; }
        public string? LocationType { get; init; }
        public string? City { get; init; }
        public required string Country { get; init; }
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }
    }

    public sealed class UpdateResponse
    {
        public Guid RouteId { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class DeleteResponse
    {
        public Guid RouteId { get; init; }
        public DateTimeOffset DeletedAt { get; init; }
    }
}
