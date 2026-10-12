namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default);

    Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default);

    Task<Response.DraftResponse> UpdateAsync(Guid accountId, Guid invoiceId,
        Request.UpdateRequest request, CancellationToken cancellationToken = default);
}
