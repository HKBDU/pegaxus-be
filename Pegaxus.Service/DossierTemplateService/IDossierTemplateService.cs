namespace Pegaxus.Service.DossierTemplateService;

public interface IDossierTemplateService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.TemplateResponse> GetAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<Response.TemplateResponse> CreateAsync(Request.SaveRequest request, CancellationToken cancellationToken = default);
    Task<Response.TemplateResponse> UpdateAsync(Guid templateId, Request.SaveRequest request, CancellationToken cancellationToken = default);
    Task<Response.TemplateResponse> ChangeStatusAsync(Guid templateId, Request.ChangeStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid templateId, Guid accountId, CancellationToken cancellationToken = default);
}
