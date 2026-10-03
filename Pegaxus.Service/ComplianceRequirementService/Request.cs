namespace Pegaxus.Service.ComplianceRequirementService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Country { get; set; }
        public string? BcpCode { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string SortBy { get; set; } = "updatedAt:desc";
    }

    public sealed class SaveRequest
    {
        public string RequirementCode { get; set; } = string.Empty;
        public string RequirementName { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = "ACTIVE";
        public string? BcpCode { get; set; }
        public string? IssuingAuthority { get; set; }
        public string? Description { get; set; }
        public string? LegalBasis { get; set; }
        public int? ValidityPeriodDays { get; set; }
        public bool? ValidityRequired { get; set; }
        public bool AcknowledgeReferenceImpact { get; set; }
    }

    public sealed class StatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
