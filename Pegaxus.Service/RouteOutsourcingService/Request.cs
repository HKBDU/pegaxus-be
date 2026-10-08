namespace Pegaxus.Service.RouteOutsourcingService;

public sealed class Request
{
    public bool? Outsourced { get; set; }
    public Guid? TransportProviderId { get; set; }
    public string? ServiceType { get; set; }
    public string? ProviderReference { get; set; }
    public string? Note { get; set; }
    public decimal? CostAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

