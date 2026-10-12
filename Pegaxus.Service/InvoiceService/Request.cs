namespace Pegaxus.Service.InvoiceService;

public static class Request
{
    public sealed class BookingListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Type { get; set; }
        public string? Status { get; set; }
        public string SortBy { get; set; } = "createdAt:desc";
    }
}
