using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.ComplianceRequirementService;

public static class Request
{
    public sealed class ListRequest
    {
        [Range(1, int.MaxValue)]
        public int PageIndex { get; set; } = 1;
        [Range(1, 100)]
        public int PageSize { get; set; } = 10;
        [MaxLength(200)]
        public string? Search { get; set; }
        public string? Country { get; set; }
        [MaxLength(50)]
        public string? BcpCode { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string SortBy { get; set; } = "updatedAt:desc";
    }

    public sealed class SaveRequest
    {
        [Required, MaxLength(50)]
        public string RequirementCode { get; set; } = string.Empty;
        [Required, MaxLength(200)]
        public string RequirementName { get; set; } = string.Empty;
        public string? Country { get; set; }
        [Required]
        public string Category { get; set; } = string.Empty;
        [Required]
        public string Status { get; set; } = "ACTIVE";
        [MaxLength(50)]
        public string? BcpCode { get; set; }
        [MaxLength(200)]
        public string? IssuingAuthority { get; set; }
        public string? Description { get; set; }
        public string? LegalBasis { get; set; }
        [Range(1, int.MaxValue)]
        public int? ValidityPeriodDays { get; set; }
        [Required]
        public bool? ValidityRequired { get; set; }
        public bool AcknowledgeReferenceImpact { get; set; }
    }

    public sealed class StatusRequest
    {
        [Required]
        public string Status { get; set; } = string.Empty;
    }
}
