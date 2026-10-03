namespace Pegaxus.Service.DossierTemplateService;

public static class Response
{
    public sealed class ListResponse
    {
        public required IReadOnlyList<TemplateResponse> Items { get; init; }
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public int TotalItems { get; init; }
        public int TotalPages { get; init; }
    }

    public sealed class TemplateResponse
    {
        public Guid DossierTemplateId { get; init; }
        public required string TemplateCode { get; init; }
        public required string TemplateName { get; init; }
        public string? Description { get; init; }
        public required string Status { get; init; }
        public int VersionNo { get; init; }
        public DateOnly? EffectiveFrom { get; init; }
        public DateOnly? EffectiveTo { get; init; }
        public bool CanDelete { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public required IReadOnlyList<ItemResponse> Items { get; init; }
    }

    public sealed class ItemResponse
    {
        public Guid DossierTemplateItemId { get; init; }
        public Guid ComplianceRequirementId { get; init; }
        public required string RequirementCode { get; init; }
        public required string RequirementName { get; init; }
        public int SequenceNo { get; init; }
        public bool IsMandatory { get; init; }
        public bool AppliesPerHorse { get; init; }
        public string? Instruction { get; init; }
    }
}
