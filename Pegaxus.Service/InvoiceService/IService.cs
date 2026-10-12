namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<IReadOnlyList<Response.StatusHistoryResponse>> GetStatusHistoryAsync(Guid invoiceId, Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Response.CancelResponse> CancelAsync(Guid invoiceId, Request.CancelRequest request, Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Response.RecordPaymentResponse> RecordPaymentAsync(Guid invoiceId, Request.RecordPaymentRequest request,
        Guid accountId, CancellationToken cancellationToken = default);

    Task<Response.IssueResponse> IssueAsync(Guid invoiceId, Request.IssueRequest request, Guid accountId,
        CancellationToken cancellationToken = default);

    Task<Response.InvoiceResponse> GetAsync(Guid accountId, Guid invoiceId, CancellationToken cancellationToken = default);

    Task<Response.ListResponse> GetByBookingAsync(Guid accountId, Guid bookingId, Request.BookingListRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default);

    Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default);

    Task<Response.DraftResponse> UpdateAsync(Guid accountId, Guid invoiceId,
        Request.UpdateRequest request, CancellationToken cancellationToken = default);
}
