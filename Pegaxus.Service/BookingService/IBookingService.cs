namespace Pegaxus.Service.BookingService;

public interface IBookingService
{
    Task<Response.NegotiatedBookingResponse> NegotiateAsync(Guid accountId, Guid bookingId, Request.NegotiateBookingRequest request, CancellationToken cancellationToken = default);

    Task<Response.BookingDetailResponse> GetAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default);
    Task<Response.ApprovedBookingDetailResponse> GetApprovedAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Response.BookingHistoryResponse>> GetHistoriesAsync(
        Guid accountId,
        Guid bookingId,
        CancellationToken cancellationToken = default);
    Task<Response.BookingListResponse> GetListAsync(
        Guid accountId,
        Request.BookingListRequest request,
        CancellationToken cancellationToken = default);
    Task<Response.ApprovedBookingListResponse> GetApprovedListAsync(
        Guid accountId,
        Request.ApprovedBookingListRequest request,
        CancellationToken cancellationToken = default);
    Task AddHorseIntoBookingAsync(
        Guid accountId,
        Guid bookingId,
        Request.AddHorseRequest request,
        CancellationToken cancellationToken = default);
    Task UpdateHorseNotesAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        Request.UpdateHorseNotesRequest request,
        CancellationToken cancellationToken = default);
    Task RemoveHorseAsync(
        Guid accountId,
        Guid bookingId,
        Guid horseId,
        CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(
        Guid accountId,
        Guid bookingId,
        Request.ChangeStatusRequest request,
        CancellationToken cancellationToken = default);
    Task<Response.CreateBookingResponse> CreateAsync(Guid accountId, Request.CreateBookingRequest request, CancellationToken cancellationToken = default);
    Task<Response.UpdateBookingResponse> UpdateAsync(Guid accountId, Guid bookingId, Request.UpdateBookingRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid accountId, Guid bookingId, Request.CancelBookingRequest request, CancellationToken cancellationToken = default);
    Task<Response.ApproveBookingResponse> ApproveAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
    Task RejectAsync(Guid accountId, Guid bookingId, Request.RejectBookingRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Response.BookingAssignmentResponse>> GetAssignmentsAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
    Task<Response.BookingAssignmentResponse> AssignEmployeeAsync(Guid accountId, Guid bookingId, Request.AssignBookingEmployeeRequest request, CancellationToken cancellationToken = default);
    Task<Response.BookingAssignmentResponse> RevokeAssignmentAsync(Guid accountId, Guid bookingId, Guid assignmentId, CancellationToken cancellationToken = default);
}
