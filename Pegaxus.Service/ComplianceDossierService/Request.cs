using Microsoft.AspNetCore.Http;

namespace Pegaxus.Service.ComplianceDossierService;

public class Request
{
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
}
