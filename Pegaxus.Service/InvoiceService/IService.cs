namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.DraftResponse> UpdateAsync(Guid accountId, Guid invoiceId,
        Request.UpdateRequest request, CancellationToken cancellationToken = default);
}
