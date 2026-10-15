namespace Pegaxus.Service.ComplianceRequirementService;

public interface IComplianceRequirementService
{
    Task<Response.ListResponse> GetListAsync(Request.ListRequest request, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> CreateAsync(Request.SaveRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task<Response.DetailResponse> UpdateAsync(Guid id, Request.SaveRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid id, Request.StatusRequest request, Guid actorAccountId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, Guid actorAccountId, CancellationToken cancellationToken = default);
}
