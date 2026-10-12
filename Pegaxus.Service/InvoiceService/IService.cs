namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.DraftResponse> CreateAsync(Guid accountId, Guid bookingId,
        Request.CreateRequest request, CancellationToken cancellationToken = default);
}
