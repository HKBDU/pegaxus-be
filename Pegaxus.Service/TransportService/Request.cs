namespace Pegaxus.Service.TransportService;

public static class Request
{
    public class AssignedRequest
    {
        public string? Status { get; set; }
    }

    public sealed class ListRequest : AssignedRequest
    {
        public string? Search { get; set; }
        public bool? HasActiveIncident { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "departureAt:asc";
    }
}
