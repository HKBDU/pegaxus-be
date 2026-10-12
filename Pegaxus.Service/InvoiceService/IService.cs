namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.OnlinePaymentResponse> InitiateOnlinePaymentAsync(Guid invoiceId, Guid accountId,
        CancellationToken cancellationToken = default);
}
