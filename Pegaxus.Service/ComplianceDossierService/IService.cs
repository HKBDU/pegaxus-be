namespace Pegaxus.Service.ComplianceDossierService;

public interface IService
{
    Task<Response.DocumentUploadResponse> SubmitDocumentVersionAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, CancellationToken cancellationToken = default);
    Task<Response.DocumentMetadataResponse> UpdateDocumentMetadataAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentMetadataRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Response.DocumentVersionResponse>> GetDocumentVersionsAsync(Guid accountId, Guid dossierId, Guid requirementId,
        CancellationToken cancellationToken = default);

    Task<Response.DocumentUploadResponse> UploadDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Request.DocumentUploadRequest request, CancellationToken cancellationToken = default);

    Task<Response.DocumentReviewResultResponse> ApproveDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId, Guid versionId,
        Request.ApproveDocumentRequest request, CancellationToken cancellationToken = default);
    Task<Response.DocumentReviewResultResponse> RevokeDocumentReviewAsync(Guid accountId, Guid dossierId, Guid requirementId, Guid versionId,
        Guid reviewId, Request.RevokeDocumentReviewRequest request, CancellationToken cancellationToken = default);
    Task<Response.DossierReadinessResponse> MarkReadyForSubmissionAsync(Guid accountId, Guid dossierId, CancellationToken cancellationToken = default);

    Task<Response.DocumentRejectionResponse> RejectDocumentAsync(Guid accountId, Guid dossierId, Guid requirementId, Guid versionId,
        Request.RejectDocumentRequest request, CancellationToken cancellationToken = default);
    Task<Response.DocumentRequestCommentResponse> AddDocumentRequestCommentAsync(Guid accountId, Guid dossierId, Guid requirementId,
        Guid requestId, Request.DocumentRequestCommentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Response.DocumentRequestCommentResponse>> GetDocumentRequestCommentsAsync(Guid accountId, Guid dossierId,
        Guid requirementId, Guid requestId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Response.RequirementResponse>> GetMissingRequirementsAsync(Guid accountId, Guid dossierId, CancellationToken cancellationToken = default);
    Task<Response.MissingDocumentsResponse> RequestMissingDocumentsAsync(Guid accountId, Guid dossierId,
        Request.MissingDocumentsRequest request, CancellationToken cancellationToken = default);
    Task<int> QueueAutomaticMissingDocumentRemindersAsync(CancellationToken cancellationToken = default);
    Task DeliverPendingMissingDocumentRemindersAsync(CancellationToken cancellationToken = default);

    Task<Response.OverviewResponse> GetByBookingAsync(Guid accountId, Guid bookingId, Request.ViewRequest request, CancellationToken cancellationToken = default);
    Task<Response.OverviewResponse> GetDossierAsync(Guid accountId, Guid dossierId, Request.ViewRequest request, CancellationToken cancellationToken = default);
    Task<Response.RequirementDetailResponse> GetRequirementAsync(Guid accountId, Guid dossierId, Guid requirementId, CancellationToken cancellationToken = default);
    Task<MediaService.PreviewFile> GetDocumentPreviewAsync(Guid accountId, Guid dossierId, Guid requirementId, Guid versionId, Guid? attachmentId, CancellationToken cancellationToken = default);

    Task<Response.RequirementResponse> AddRequirementAsync(Guid accountId, Guid dossierId, Request.RequirementRequest request, CancellationToken cancellationToken = default);
    Task<Response.RequirementResponse> UpdateRequirementAsync(Guid accountId, Guid dossierId, Guid requirementId, Request.UpdateRequirementRequest request, CancellationToken cancellationToken = default);
    Task<Response.RemovalResponse> RemoveRequirementAsync(Guid accountId, Guid dossierId, Guid requirementId, Request.RemoveRequirementRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Response.TemplateOptionResponse>> GetTemplateOptionsAsync(Guid accountId, Guid bookingId, CancellationToken cancellationToken = default);
    Task<Response.InitializationResponse> PreviewAsync(Guid accountId, Guid bookingId, Request.InitializeRequest request, CancellationToken cancellationToken = default);
    Task<Response.InitializationResponse> InitializeAsync(Guid accountId, Guid bookingId, Request.InitializeRequest request, CancellationToken cancellationToken = default);

    Task<Response.DashboardResponse> GetDashboardAsync(
        Guid accountId,
        Request.DashboardRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.DashboardExportResponse> ExportDashboardAsync(
        Guid accountId,
        Request.DashboardExportRequest request,
        CancellationToken cancellationToken = default);

    Task<Response.AuditHistoryResponse> GetAuditHistoryAsync(
        Guid accountId,
        Guid dossierId,
        Request.AuditHistoryRequest request,
        CancellationToken cancellationToken = default);

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

    Task<Response.CustomsClearanceProofFinalizeResponse> FinalizeCustomsClearanceProofAsync(
        Guid accountId,
        Guid proofId,
        Request.FinalizeCustomsClearanceProofRequest request,
        CancellationToken cancellationToken = default);
}
