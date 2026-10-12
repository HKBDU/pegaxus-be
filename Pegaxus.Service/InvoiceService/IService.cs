namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.RecordPaymentResponse> RecordPaymentAsync(Guid invoiceId, Request.RecordPaymentRequest request,
        Guid accountId, CancellationToken cancellationToken = default);
}
