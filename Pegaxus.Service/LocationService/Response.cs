namespace Pegaxus.Service.LocationService;

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
        public Guid LocationId { get; init; }
        public required string LocationName { get; init; }
        public string? LocationType { get; init; }
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? StateProvince { get; init; }
        public required string Country { get; init; }
        public decimal? Latitude { get; init; }
        public decimal? Longitude { get; init; }
        public string? LocationNote { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class UpdateResponse
    {
        public Guid LocationId { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class DeleteResponse
    {
        public Guid LocationId { get; init; }
        public DateTimeOffset DeletedAt { get; init; }
    }
}
