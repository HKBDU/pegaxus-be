namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.ListResponse> GetByBookingAsync(Guid accountId, Guid bookingId, Request.BookingListRequest request,
        CancellationToken cancellationToken = default);
}
