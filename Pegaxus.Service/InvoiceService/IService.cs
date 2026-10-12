namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.SuggestionResponse> GetSuggestionAsync(Guid accountId, Guid bookingId,
        Request.SuggestionRequest request, CancellationToken cancellationToken = default);
}
