namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Type { get; set; }
        public string? Status { get; set; }
        public Guid? BookingId { get; set; }
        public Guid? TransportPlanId { get; set; }
        public DateOnly? IssuedFrom { get; set; }
        public DateOnly? IssuedTo { get; set; }
        public DateOnly? DueFrom { get; set; }
        public DateOnly? DueTo { get; set; }
        public string? Search { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }
}
