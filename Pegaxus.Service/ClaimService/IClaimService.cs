namespace Pegaxus.Service.ClaimService;

public interface IClaimService
{
    Task<Response.Eligibility> GetEligibilityAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);

    Task<Response.ClaimResponse> SubmitAsync(Guid accountId, Guid bookingId, Request.SubmitRequest request, CancellationToken cancellationToken = default);

    Task<Response.ListResponse> GetListAsync(Guid accountId, Request.ListRequest request, CancellationToken cancellationToken = default);

    Task<Response.ClaimResponse> GetAsync(Guid accountId, Guid claimId, CancellationToken cancellationToken = default);

    Task<Response.ClaimResponse> ResolveAsync(Guid accountId, Guid claimId, Request.ResolveRequest request, CancellationToken cancellationToken = default);

    Task<Response.ClaimResponse> CancelAsync(Guid accountId, Guid claimId, Request.CancelRequest request, CancellationToken cancellationToken = default);

    Task<Response.ClaimResponse> CompleteAsync(Guid accountId, Guid claimId, Request.CompleteRequest request, CancellationToken cancellationToken = default);
}
