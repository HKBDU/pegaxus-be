namespace Pegaxus.Service.RouteOutsourcingService;

public sealed class Request
{
    public bool? Outsourced { get; set; }
    public Guid? TransportProviderId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

