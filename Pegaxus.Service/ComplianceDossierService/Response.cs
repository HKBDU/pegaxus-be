using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.ComplianceDossierService;

public class Response
{
    public sealed class DocumentUploadResponse
    {
        public Guid DossierRequirementId { get; init; }
        public Guid ComplianceDocumentId { get; init; }
        public Guid DocumentVersionId { get; init; }
        public int VersionNo { get; init; }
        public bool IsNewVersion { get; init; }
        public string Status { get; init; } = string.Empty;
        public IReadOnlyList<UploadedDocumentFileResponse> Files { get; init; } = [];
    }

    public sealed class UploadedDocumentFileResponse
    {
        public Guid? DocumentAttachmentId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public long FileSizeBytes { get; init; }
        public string ReadUrl { get; init; } = string.Empty;
        public DateTimeOffset ReadUrlExpiresAt { get; init; }
    }

    public sealed class DocumentMetadataResponse
    {
        public Guid ComplianceDocumentId { get; init; }
        public string? DocumentNumber { get; init; }
        public string? IssuingAuthority { get; init; }
        public DateOnly? IssuedDate { get; init; }
        public DateOnly? ExpiryDate { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    public sealed class DocumentVersionResponse
    {
        public Guid DocumentVersionId { get; init; }
        public int VersionNo { get; init; }
        public Guid UploadedByAccountId { get; init; }
        public DateTime UploadedAt { get; init; }
        public string? UploadNote { get; init; }
        public IReadOnlyList<UploadedDocumentFileResponse> Files { get; init; } = [];
        public IReadOnlyList<DocumentReviewResponse> Reviews { get; init; } = [];
    }

    public sealed class DocumentReviewResponse
    {
        public Guid DocumentReviewId { get; init; }
        public Guid ReviewedByAccountId { get; init; }
        public string Result { get; init; } = string.Empty;
        public string? ReviewComment { get; init; }
        public DateTime ReviewedAt { get; init; }
    }

    public sealed class DocumentReviewResultResponse
    {
        public Guid DocumentVersionId { get; init; }
        public Guid DossierRequirementId { get; init; }
        public ReviewResponse Review { get; init; } = null!;
        public string RequirementStatus { get; init; } = string.Empty;
        public string DocumentStatus { get; init; } = string.Empty;
        public string DossierStatus { get; init; } = string.Empty;
    }

    public sealed class DossierReadinessResponse
    {
        public Guid ComplianceDossierId { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTime? ReadyForSubmissionAt { get; init; }
    }

    public sealed class DocumentRejectionResponse
    {
        public ReviewResponse Review { get; init; } = null!;
        public DocumentRequestResponse DocumentRequest { get; init; } = null!;
        public string RequirementStatus { get; init; } = string.Empty;
        public string DocumentStatus { get; init; } = string.Empty;
        public string DossierStatus { get; init; } = string.Empty;
        public bool EmailSent { get; set; }
        public string? DeliveryWarning { get; set; }
    }

    public sealed class MissingDocumentsResponse
    {
        public Guid ReminderId { get; init; }
        public IReadOnlyList<RequirementResponse> Requirements { get; init; } = [];
        public bool EmailSent { get; set; }
        public string? DeliveryWarning { get; set; }
    }

    public sealed class DocumentRequestResponse
    {
        public Guid DocumentRequestId { get; init; }
        public Guid? DocumentReviewId { get; init; }
        public Guid DossierRequirementId { get; init; }
        public Guid RequestedByAccountId { get; init; }
        public string RequestCode { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? ErrorCategory { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateOnly? DueDate { get; init; }
        public DateTime RequestedAt { get; init; }
    }

    public sealed class DocumentRequestCommentResponse
    {
        public Guid DocumentRequestCommentId { get; init; }
        public Guid AuthorAccountId { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }

    public sealed class OverviewResponse
    {
        public bool IsInitialized { get; init; }
        public string? Message { get; init; }
        public Guid BookingId { get; init; }
        public string BookingCode { get; init; } = string.Empty;
        public Guid? ComplianceDossierId { get; init; }
        public string? DossierCode { get; init; }
        public string? Status { get; init; }
        public int FulfilledCount { get; init; }
        public int RequirementCount { get; init; }
        public decimal CompletionPercent { get; init; }
        public IReadOnlyList<RequirementResponse> TripRequirements { get; init; } = [];
        public IReadOnlyList<HorseRequirementsResponse> HorseRequirements { get; init; } = [];
        public ActionsResponse Actions { get; init; } = new();
    }

    public sealed class HorseRequirementsResponse
    {
        public Guid HorseId { get; init; }
        public string HorseName { get; init; } = string.Empty;
        public IReadOnlyList<RequirementResponse> Requirements { get; init; } = [];
    }

    public sealed class ActionsResponse
    {
        public bool CanUpload { get; init; }
        public bool CanReview { get; init; }
    }

    public sealed class RequirementDetailResponse
    {
        public RequirementResponse Requirement { get; init; } = null!;
        public DateOnly? ValidUntil { get; init; }
        public DateTime? SatisfiedAt { get; init; }
        public string? WaivedReason { get; init; }
        public DocumentResponse? Document { get; init; }
        public IReadOnlyList<DocumentRequestResponse> DocumentRequests { get; init; } = [];
        public ActionsResponse Actions { get; init; } = new();
    }

    public sealed class DocumentResponse
    {
        public Guid ComplianceDocumentId { get; init; }
        public string DocumentCode { get; init; } = string.Empty;
        public string DocumentName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? DocumentNumber { get; init; }
        public string? IssuingAuthority { get; init; }
        public DateOnly? IssuedDate { get; init; }
        public DateOnly? ExpiryDate { get; init; }
        public string? Note { get; init; }
        public DateTime UpdatedAt { get; init; }
        public IReadOnlyList<VersionResponse> Versions { get; init; } = [];
    }

    public sealed class VersionResponse
    {
        public Guid DocumentVersionId { get; init; }
        public int VersionNo { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string PreviewUrl { get; init; } = string.Empty;
        public string? MimeType { get; init; }
        public long? FileSizeBytes { get; init; }
        public string? UploadNote { get; init; }
        public DateTime UploadedAt { get; init; }
        public IReadOnlyList<AttachmentResponse> Attachments { get; init; } = [];
        public IReadOnlyList<ReviewResponse> Reviews { get; init; } = [];
    }

    public sealed class AttachmentResponse
    {
        public Guid DocumentAttachmentId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string PreviewUrl { get; init; } = string.Empty;
        public string? MimeType { get; init; }
        public long? FileSizeBytes { get; init; }
        public int SortOrder { get; init; }
    }

    public sealed class ReviewResponse
    {
        public Guid DocumentReviewId { get; init; }
        public Guid ReviewedByAccountId { get; init; }
        public string Result { get; init; } = string.Empty;
        public string? ReviewComment { get; init; }
        public DateTime ReviewedAt { get; init; }
    }

    public sealed class RequirementResponse
    {
        public Guid DossierRequirementId { get; init; }
        public Guid ComplianceRequirementId { get; init; }
        public string RequirementCode { get; init; } = string.Empty;
        public string RequirementName { get; init; } = string.Empty;
        public Guid? HorseId { get; init; }
        public string? HorseName { get; init; }
        public bool IsMandatory { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateOnly? DueDate { get; init; }
        public int? LeadTimeDays { get; init; }
        public string? Instruction { get; init; }
        public DocumentRequestResponse? OpenDocumentRequest { get; set; }
    }

    public sealed class RemovalResponse
    {
        public Guid DossierRequirementId { get; init; }
        public int FulfilledCount { get; init; }
        public int RequirementCount { get; init; }
        public decimal CompletionPercent { get; init; }
    }

    public sealed class TemplateOptionResponse
    {
        public Guid DossierTemplateId { get; init; }
        public string TemplateCode { get; init; } = string.Empty;
        public string TemplateName { get; init; } = string.Empty;
        public int VersionNo { get; init; }
        public int MatchingRequirements { get; init; }
    }

    public sealed class InitializationResponse
    {
        public Guid? ComplianceDossierId { get; init; }
        public string? DossierCode { get; init; }
        public Guid BookingId { get; init; }
        public DateTimeOffset BookingUpdatedAt { get; init; }
        public Guid TransportId { get; init; }
        public Guid? DossierTemplateId { get; init; }
        public string Status { get; init; } = "IN_PREPARATION";
        public IReadOnlyList<Request.InitializationItemRequest> Items { get; init; } = [];
        public IReadOnlyList<GeneratedRequirementResponse> Requirements { get; init; } = [];
    }

    public sealed class GeneratedRequirementResponse
    {
        public Guid? DossierRequirementId { get; set; }
        public Guid ComplianceRequirementId { get; init; }
        public string RequirementCode { get; init; } = string.Empty;
        public string RequirementName { get; init; } = string.Empty;
        public Guid? HorseId { get; init; }
        public string? HorseName { get; init; }
        public bool IsMandatory { get; init; }
        public string? Instruction { get; init; }
        public DateOnly? ValidUntil { get; init; }
    }

    public class AuthoritySubmissionListResponse
    {
        public IReadOnlyList<AuthoritySubmissionItemResponse> Items { get; init; } = [];
    }

    public class AuthoritySubmissionItemResponse
    {
        public Guid AuthoritySubmissionId { get; init; }

        public int SubmissionNo { get; init; }

        public string AuthorityName { get; init; } = string.Empty;

        public string? SubmissionReference { get; init; }

        public string? SubmissionMethod { get; init; }

        public string Status { get; init; } = string.Empty;

        public bool IsRequired { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public DateTimeOffset? ResponseReceivedAt { get; init; }

        public string? PermitNumber { get; init; }

        public DateOnly? PermitIssuedDate { get; init; }

        public DateOnly? PermitExpiryDate { get; init; }

        public DateOnly? RevisionDueDate { get; init; }

        public string? DecisionNote { get; init; }

        public string? RejectionReason { get; init; }

        public SubmittedByResponse SubmittedBy { get; init; } = null!;

        public IReadOnlyList<EvidenceFileResponse> EvidenceFiles { get; init; } = [];
    }

    public class SubmittedByResponse
    {
        public Guid AccountId { get; init; }

        public string FullName { get; init; } = string.Empty;
    }

    public class EvidenceFileResponse
    {
        public string EvidenceType { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public string FileUrl { get; init; } = string.Empty;

        public string? MimeType { get; init; }

        public long? FileSizeBytes { get; init; }

        public string? Note { get; init; }
    }

    public class AuthoritySubmissionResponse
    {
        public Guid AuthoritySubmissionId { get; init; }

        public int SubmissionNo { get; init; }

        public string Status { get; init; } = string.Empty;

        public string DossierStatus { get; init; } = string.Empty;
    }

    public class AuthorityDecisionResponse
    {
        public Guid AuthoritySubmissionId { get; init; }

        public string SubmissionStatus { get; init; } = string.Empty;

        public string DossierStatus { get; init; } = string.Empty;
    }

    public class QuarantineProofResponse
    {
        public Guid QuarantineProofId { get; init; }

        public Guid ComplianceDossierId { get; init; }

        public string Status { get; init; } = string.Empty;

        public string BcpName { get; init; } = string.Empty;

        public Guid? LocationId { get; init; }

        public DateTimeOffset? InspectionAt { get; init; }

        public string? SubmittedNote { get; init; }

        public QuarantineEmployeeResponse SubmittedBy { get; init; } = null!;

        public DateTimeOffset SubmittedAt { get; init; }

        public string? OfficialVeterinarianName { get; init; }

        public string? BcpReceiptNumber { get; init; }

        public string? Conditions { get; init; }

        public object? MicrochipChecklist { get; init; }

        public QuarantineEmployeeResponse? VerifiedBy { get; init; }

        public DateTimeOffset? VerifiedAt { get; init; }

        public string? VerificationNote { get; init; }

        public string? RejectionReason { get; init; }

        public bool IsFinalized { get; init; }

        public DateTimeOffset? FinalizedAt { get; init; }

        public IReadOnlyList<ExpectedHorseResponse> ExpectedHorses { get; init; } = [];

        public IReadOnlyList<ProofFileResponse> Files { get; init; } = [];
    }

    public class QuarantineEmployeeResponse
    {
        public Guid EmployeeId { get; init; }

        public string FullName { get; init; } = string.Empty;
    }

    public class ExpectedHorseResponse
    {
        public Guid HorseId { get; init; }

        public string HorseName { get; init; } = string.Empty;

        public string? MicrochipNumber { get; init; }
    }

    public class ProofFileResponse
    {
        public string FileName { get; init; } = string.Empty;

        public string FileUrl { get; init; } = string.Empty;

        public string? MimeType { get; init; }

        public long? FileSizeBytes { get; init; }

        public int SortOrder { get; init; }
    }

    public class QuarantineProofFinalizeResponse
    {
        public Guid QuarantineProofId { get; init; }

        public string Status { get; init; } = string.Empty;

        public bool IsFinalized { get; init; }
    }

    public class CustomsClearanceProofResponse
    {
        public Guid CustomsClearanceProofId { get; init; }

        public Guid ComplianceDossierId { get; init; }

        public string Status { get; init; } = string.Empty;

        public string CustomsPortName { get; init; } = string.Empty;

        public Guid? LocationId { get; init; }

        public string? MrnNumber { get; init; }

        public string? CustomsLane { get; init; }

        public string? SubmittedNote { get; init; }

        public DateOnly? AtaCarnetExpiryDate { get; init; }

        public AtaCarnetResponse? AtaCarnet { get; init; }

        public DateTimeOffset? ClearanceAt { get; init; }

        public CustomsEmployeeResponse SubmittedBy { get; init; } = null!;

        public DateTimeOffset SubmittedAt { get; init; }

        public CustomsEmployeeResponse? VerifiedBy { get; init; }

        public DateTimeOffset? VerifiedAt { get; init; }

        public string? VerificationNote { get; init; }

        public string? RejectionReason { get; init; }

        public bool IsFinalized { get; init; }

        public DateTimeOffset? FinalizedAt { get; init; }

        public IReadOnlyList<ProofFileResponse> Files { get; init; } = [];
    }

    public class AtaCarnetResponse
    {
        public Guid ComplianceDocumentId { get; init; }

        public DateOnly? ExpiryDate { get; init; }

        public string Status { get; init; } = string.Empty;
    }

    public class CustomsEmployeeResponse
    {
        public Guid EmployeeId { get; init; }

        public string FullName { get; init; } = string.Empty;
    }

    public class CustomsClearanceProofFinalizeResponse
    {
        public Guid CustomsClearanceProofId { get; init; }

        public string Status { get; init; } = string.Empty;

        public bool IsFinalized { get; init; }
    }

    public class DashboardResponse
    {
        public DashboardKpiResponse Kpi { get; init; } = null!;

        public IReadOnlyList<DashboardItemResponse> Items { get; init; } = [];

        public int PageIndex { get; init; }

        public int PageSize { get; init; }

        public int TotalItems { get; init; }

        public int TotalPages { get; init; }
    }

    public class DashboardKpiResponse
    {
        public int TotalActive { get; init; }

        public int ReadyForDeparture { get; init; }

        public int ActionRequired { get; init; }

        public int SubmittedToAuthorities { get; init; }

        public int UrgentRisk { get; init; }
    }

    public class DashboardItemResponse
    {
        public Guid ComplianceDossierId { get; init; }

        public string BookingCode { get; init; } = string.Empty;

        public string CustomerName { get; init; } = string.Empty;

        public string OriginCountry { get; init; } = string.Empty;

        public string DestinationCountry { get; init; } = string.Empty;

        public DateOnly RequestedDepartureDate { get; init; }

        public decimal ProgressPercent { get; init; }

        public string Status { get; init; } = string.Empty;

        public bool IsUrgentRisk { get; init; }
    }

    public class DashboardExportResponse
    {
        public byte[] Content { get; init; } = [];

        public string ContentType { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;
    }

    public class AuditHistoryResponse
    {
        public IReadOnlyList<AuditHistoryItemResponse> Items { get; init; } = [];
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public class AuditHistoryItemResponse
    {
        public string ActionType { get; init; } = string.Empty;
        public string TargetEntity { get; init; } = string.Empty;
        public Guid? TargetEntityId { get; init; }
        public AuditActorResponse Actor { get; init; } = null!;
        public string? Note { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
    }

    public class AuditActorResponse
    {
        public Guid AccountId { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
    }
}
