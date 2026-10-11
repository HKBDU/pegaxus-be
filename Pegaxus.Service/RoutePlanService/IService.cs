namespace Pegaxus.Service.RoutePlanService;

public interface IService
{
    Task<Response.DetailResponse> CreateAsync(Guid accountId, Guid transportPlanId, Request.SaveRequest request, CancellationToken cancellationToken = default);
    Task<Response.ListResponse> GetListAsync(Guid accountId, Guid transportPlanId, Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetByIdAsync(Guid accountId, Guid routePlanId, CancellationToken cancellationToken = default);
    Task<Response.UpdateResponse> UpdateAsync(Guid accountId, Guid routePlanId, Request.UpdateRequest request, CancellationToken cancellationToken = default);
    Task<Response.CancelResponse> CancelAsync(Guid accountId, Guid routePlanId, Request.CancelRequest request, CancellationToken cancellationToken = default);
    Task<Response.ExportResponse> ExportAsync(Guid accountId, Guid routePlanId, string? format, CancellationToken cancellationToken = default);
    Task<Response.LegalReviewResultResponse> ReviewLegalAsync(Guid accountId, Guid routePlanId, Request.LegalReviewRequest request, CancellationToken cancellationToken = default);
}
