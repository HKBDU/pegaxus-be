namespace Pegaxus.Service.TransportProviderService;

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
        public Guid TransportProviderId { get; init; }
        public required string ProviderCode { get; init; }
        public required string ProviderName { get; init; }
        public required string Status { get; init; }
        public required string[] SupportedModes { get; init; }
        public string? TaxCode { get; init; }
        public string? ContactName { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? Address { get; init; }
        public string? Country { get; init; }
        public string? Note { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public class WriteResponse
    {
        public Guid TransportProviderId { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class StatusResponse : WriteResponse
    {
        public required string Status { get; init; }
    }
}
