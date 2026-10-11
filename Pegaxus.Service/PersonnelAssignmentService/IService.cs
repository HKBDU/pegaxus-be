namespace Pegaxus.Service.PersonnelAssignmentService;

public interface IService
{
    Task<Response.AssignableResponse> GetAssignableAsync(Request.AssignableRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.AssignmentsResponse> GetAsync(Guid transportPlanId, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.AssignResponse> AssignAsync(Guid routePlanId, Request.AssignRequest request, Guid accountId, CancellationToken cancellationToken = default);
    Task<Response.ReplaceResponse> ReplaceAsync(Guid routePlanId, Guid routePlanRouteId, Request.ReplaceRequest request, Guid accountId, CancellationToken cancellationToken = default);
}
