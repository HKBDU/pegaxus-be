namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.CancelResponse> CancelAsync(Guid invoiceId, Request.CancelRequest request, Guid accountId,
        CancellationToken cancellationToken = default);
}
