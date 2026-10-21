namespace Pegaxus.Service.TransportProviderService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Status { get; set; }
        public string? SupportedMode { get; set; }
        public string? Country { get; set; }
        public string? Search { get; set; }
        public string SortBy { get; set; } = "providerName:asc";
    }

    public class SaveRequest
    {
        public string ProviderName { get; set; } = string.Empty;
        public string TaxCode { get; set; } = string.Empty;
        public List<string> SupportedModes { get; set; } = [];
        public string? ContactName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Country { get; set; }
        public string? Note { get; set; }
    }

    public sealed class CreateRequest : SaveRequest;

    public sealed class UpdateRequest : SaveRequest
    {
        public DateTimeOffset LastUpdatedAt { get; set; }
    }

    public sealed class StatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
    }
}
