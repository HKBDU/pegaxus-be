namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.InvoiceResponse> GetAsync(Guid accountId, Guid invoiceId, CancellationToken cancellationToken = default);
}
