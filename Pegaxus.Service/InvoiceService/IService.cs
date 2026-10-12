namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<IReadOnlyList<Response.StatusHistoryResponse>> GetStatusHistoryAsync(Guid invoiceId, Guid accountId,
        CancellationToken cancellationToken = default);
}
