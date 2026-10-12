namespace Pegaxus.Service.InvoiceService;

public interface IService
{
    Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request,
        CancellationToken cancellationToken = default);
}
