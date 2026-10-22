
namespace Pegaxus.Service.LocationService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }
        public string? Country { get; set; }
        public string? LocationType { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
    }

    public class CreateRequest
    {
        public string? LocationName { get; set; }
        public string? LocationType { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? StateProvince { get; set; }
        public string? Country { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? LocationNote { get; set; }
    }

    public sealed class UpdateRequest : CreateRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
