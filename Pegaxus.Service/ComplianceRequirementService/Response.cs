namespace Pegaxus.Service.ComplianceRequirementService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<DetailResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class DetailResponse
    {
        public Guid ComplianceRequirementId { get; init; }
        public required string RequirementCode { get; init; }
        public required string RequirementName { get; init; }
        public required string Status { get; init; }
        public required string Category { get; init; }
        public string? Country { get; init; }
        public string? BcpCode { get; init; }
        public string? IssuingAuthority { get; init; }
        public string? Description { get; init; }
        public string? LegalBasis { get; init; }
        public int? ValidityPeriodDays { get; init; }
        public bool ValidityRequired { get; init; }
        public bool IsReferenced => ReferenceCount.DossierTemplateItemCount > 0 || ReferenceCount.DossierRequirementCount > 0;
        public bool CanDelete => !IsReferenced;
        public required ReferenceCounts ReferenceCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }

    public sealed class ReferenceCounts
    {
        public int DossierTemplateItemCount { get; init; }
        public int DossierRequirementCount { get; init; }
        public int LinkedDossierCount { get; init; }
    }
}
