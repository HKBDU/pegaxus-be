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
}
