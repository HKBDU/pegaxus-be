using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.ComplianceDossierService;

public class Request
{
    public sealed class DocumentUploadRequest : DocumentMetadataRequest
    {
        public List<IFormFile> Files { get; set; } = [];
        public string? UploadNote { get; set; }
    }

    public class DocumentMetadataRequest
    {
        public string? DocumentNumber { get; set; }
        public string? IssuingAuthority { get; set; }
        public DateOnly? IssuedDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }

    public sealed class RequirementRequest
    {
        public Guid ComplianceRequirementId { get; set; }
        public Guid? HorseId { get; set; }
        public bool IsMandatory { get; set; } = true;
        public int LeadTimeDays { get; set; }
        public string? Instruction { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class UpdateRequirementRequest
    {
        public bool? IsMandatory { get; set; }
        public int? LeadTimeDays { get; set; }
        public string? Instruction { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class RemoveRequirementRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class InitializeRequest
    {
        public Guid? DossierTemplateId { get; set; }
        public DateTimeOffset? BookingUpdatedAt { get; set; }
        public List<InitializationItemRequest>? Items { get; set; }
        public string? Note { get; set; }
    }

    public sealed class InitializationItemRequest
    {
        public Guid ComplianceRequirementId { get; set; }
        public bool IsMandatory { get; set; } = true;
        public bool AppliesPerHorse { get; set; }
        public string? Instruction { get; set; }
    }

    public class RecordAuthoritySubmissionRequest
    {
        public string AuthorityName { get; set; } = string.Empty;

        public string SubmissionReference { get; set; } = string.Empty;

        public string? SubmissionMethod { get; set; }

        public DateTimeOffset SubmittedAt { get; set; }

        public bool IsRequired { get; set; } = true;

        public IFormFile? ProofFile { get; set; }

        public string? DecisionNote { get; set; }
    }

    public class RecordAuthorityDecisionRequest
    {
        public string Decision { get; set; } = string.Empty;

        public string? PermitNumber { get; set; }

        public DateOnly? PermitIssuedDate { get; set; }

        public DateOnly? PermitExpiryDate { get; set; }

        public IFormFile? DecisionFile { get; set; }

        public string? DecisionNote { get; set; }

        public string? RejectionReason { get; set; }

        public DateOnly? RevisionDueDate { get; set; }
    }

    public class SubmitQuarantineProofRequest
    {
        public string BcpName { get; set; } = string.Empty;

        public Guid? LocationId { get; set; }

        public DateTimeOffset? InspectionAt { get; set; }

        public bool IsHoldback { get; set; }

        public string? QuickNote { get; set; }

        public List<IFormFile> Files { get; set; } = [];
    }

    public class FinalizeQuarantineProofRequest
    {
        public string Action { get; set; } = string.Empty;

        public string? OfficialVetName { get; set; }

        public string? BcpReceiptNo { get; set; }

        public string? Status { get; set; }

        public string? Conditions { get; set; }

        public string? MicrochipChecklist { get; set; }

        public string? RejectionReason { get; set; }
    }

    public class SubmitCustomsClearanceProofRequest
    {
        public string CustomsPortName { get; set; } = string.Empty;

        public Guid? LocationId { get; set; }

        public string? MrnNumber { get; set; }

        public string? CustomsLane { get; set; }

        public bool IsHeld { get; set; }

        public string? QuickNote { get; set; }

        public List<IFormFile> Files { get; set; } = [];
    }

    public class FinalizeCustomsClearanceProofRequest
    {
        public string Action { get; set; } = string.Empty;

        public string? MrnNumber { get; set; }

        public string? CustomsLane { get; set; }

        public DateOnly? AtaCarnetExpiryDate { get; set; }

        public string? Status { get; set; }

        public string? VerificationNote { get; set; }

        public string? RejectionReason { get; set; }
    }

    public class DashboardRequest
    {
        public int PageIndex { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public string? Status { get; set; }

        public string? Risk { get; set; }

        public string? RouteType { get; set; }

        public string? SortBy { get; set; } = "requestedDepartureDate:asc";

        public string? Search { get; set; }
    }

    public sealed class DashboardExportRequest : DashboardRequest
    {
        public string Format { get; set; } = string.Empty;
    }

    public sealed class AuditHistoryRequest
    {
        public string? ActionType { get; set; }
        public string? Search { get; set; }
        public DateTimeOffset? From { get; set; }
        public DateTimeOffset? To { get; set; }
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SortBy { get; set; } = "occurredAt:desc";
    }
}
