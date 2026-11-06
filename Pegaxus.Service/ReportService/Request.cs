namespace Pegaxus.Service.ReportService;

public static class Request
{
    /// <summary>The filters every management report shares (dates are UTC days, both included).</summary>
    public class ReportFilter
    {
        public DateOnly? DateFrom { get; set; }
        public DateOnly? DateTo { get; set; }
        public string? OriginCountry { get; set; }
        public string? DestinationCountry { get; set; }
        public Guid? RouteId { get; set; }
        public Guid? TransportProviderId { get; set; }
    }

    public class PagedFilter : ReportFilter
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class OnTimeRequest : PagedFilter
    {
        public Guid? DriverEmployeeId { get; set; }
        public string? Bucket { get; set; }
        public string TrendBy { get; set; } = "WEEK";
        public string SortBy { get; set; } = "delayMinutes:desc";
    }

    public sealed class IncidentClaimRequest : PagedFilter
    {
        public string? IncidentCategory { get; set; }
        public string? Severity { get; set; }
        public Guid? VehicleId { get; set; }
        public Guid? HorseId { get; set; }
        public string? ClaimType { get; set; }
        public string SortBy { get; set; } = "occurredAt:desc";
    }

    public sealed class TransitTimeRequest : PagedFilter
    {
        public string? DelayFilter { get; set; }
        public string SortBy { get; set; } = "deltaDays:desc";
    }

    /// <summary>RA4 takes the filters of whichever report it exports, without paging.</summary>
    public sealed class ExportRequest : ReportFilter
    {
        public string? Format { get; set; }
        public Guid? DriverEmployeeId { get; set; }
        public string? Bucket { get; set; }
        public string? TrendBy { get; set; }
        public string? IncidentCategory { get; set; }
        public string? Severity { get; set; }
        public Guid? VehicleId { get; set; }
        public Guid? HorseId { get; set; }
        public string? ClaimType { get; set; }
        public string? DelayFilter { get; set; }
        public string? SortBy { get; set; }
    }
}
