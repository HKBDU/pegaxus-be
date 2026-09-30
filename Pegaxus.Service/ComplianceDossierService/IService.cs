namespace Pegaxus.Service.ComplianceDossierService;

public interface IService
{
    Task<Response.AuthoritySubmissionListResponse> GetAuthoritySubmissionsAsync(
        Guid accountId,
        Guid dossierId,
        CancellationToken cancellationToken = default);

    Task<Response.AuthoritySubmissionResponse> RecordAuthoritySubmissionAsync(
        Guid accountId,
        Guid dossierId,
        Request.RecordAuthoritySubmissionRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.AuthorityDecisionResponse> RecordAuthorityDecisionAsync(
        Guid accountId,
        Guid submissionId,
        Request.RecordAuthorityDecisionRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> SubmitQuarantineProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitQuarantineProofRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.QuarantineProofResponse> GetQuarantineProofAsync(
        Guid accountId,
        Guid proofId,
        CancellationToken cancellationToken = default);

    Task<Response.QuarantineProofFinalizeResponse> FinalizeQuarantineProofAsync(
        Guid accountId,
        Guid proofId,
        Request.FinalizeQuarantineProofRequest request,
        CancellationToken cancellationToken = default);

    Task<Guid> SubmitCustomsClearanceProofAsync(
        Guid accountId,
        Guid dossierId,
        Request.SubmitCustomsClearanceProofRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.CustomsClearanceProofResponse> GetCustomsClearanceProofAsync(
        Guid accountId,
        Guid proofId,
        CancellationToken cancellationToken = default);
}
