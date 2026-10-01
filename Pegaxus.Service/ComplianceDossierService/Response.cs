using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.ComplianceDossierService;

public class Response
{
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
}
