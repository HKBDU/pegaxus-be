namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.IssueResponse> IssueAsync(Guid invoiceId, Request.IssueRequest request, Guid accountId,
        CancellationToken cancellationToken = default);
}
