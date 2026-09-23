namespace Pegaxus.Service.BookingService;

public interface IService
{
    Task<Response.CreateBookingResponse> CreateAsync(Guid accountId, Request.CreateBookingRequest request, CancellationToken cancellationToken = default);
    Task<Response.UpdateBookingResponse> UpdateAsync(Guid accountId, Guid bookingId, Request.UpdateBookingRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid accountId, Guid bookingId, Request.CancelBookingRequest request, CancellationToken cancellationToken = default);
    Task<Response.ApproveBookingResponse> ApproveAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid accountId, Guid bookingId, Request.RejectBookingRequest request, CancellationToken cancellationToken = default);
}
