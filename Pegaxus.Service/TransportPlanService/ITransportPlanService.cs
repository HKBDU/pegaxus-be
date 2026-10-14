namespace Pegaxus.Service.TransportPlanService;

public interface ITransportPlanService
{
    Task<Response.CreateResponse> UpdateAsync(
        Guid accountId,
        Guid transportPlanId,
        Request.UpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.CreateResponse> GetByIdAsync(
        Guid accountId,
        Guid transportPlanId,
        CancellationToken cancellationToken = default);

    Task<Response.ListResponse> GetListAsync(
        Guid accountId,
        Request.ListRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.CreateResponse> CreateAsync(
        Guid accountId,
        Guid bookingId,
        Request.CreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.Phase1AssignmentResponse> AssignPhase1Async(
        Guid accountId,
        Guid transportPlanId,
        Request.Phase1AssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.Phase1AssignmentResponse> UpdatePhase1Async(
        Guid accountId,
        Guid transportPlanId,
        Request.UpdatePhase1AssignmentRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.AssignmentsResponse> GetAssignmentsAsync(
        Guid accountId,
        Guid transportPlanId,
        CancellationToken cancellationToken = default);
}
