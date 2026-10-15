namespace Pegaxus.Service.RouteService;

public static class Request
{
    public sealed class ListRequest
    {
        public Guid? FromLocationId { get; set; }
        public Guid? ToLocationId { get; set; }
        public string? TransportMode { get; set; }
        public string? Search { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class CreateRequest
    {
        public Guid FromLocationId { get; set; }
        public Guid ToLocationId { get; set; }
        public string? RouteName { get; set; }
        public string? TransportMode { get; set; }
        public string? RouteNote { get; set; }
    }

    public sealed class UpdateRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
        public Guid? FromLocationId { get; set; }
        public Guid? ToLocationId { get; set; }
        public string? RouteName { get; set; }
        public string? TransportMode { get; set; }
        public string? RouteNote { get; set; }
    }
}
