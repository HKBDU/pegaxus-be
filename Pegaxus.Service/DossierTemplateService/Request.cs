namespace Pegaxus.Service.DossierTemplateService;

public static class Request
{
    public sealed class ListRequest
    {
        public int PageIndex { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? Search { get; set; }
        public string? Status { get; set; }
        /// <summary>Ranks the templates for this booking route and adds the recommendation fields; only ACTIVE templates that can be applied are listed.</summary>
        public Guid? BookingId { get; set; }
        /// <summary>Only <c>recommendation:desc</c> is acted on, and it needs <see cref="BookingId"/>; any other value keeps the default order.</summary>
        public string? SortBy { get; set; }
    }

    public sealed class SaveRequest
    {
        public string TemplateCode { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = "DRAFT";
        public DateOnly? EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public List<ItemRequest> Items { get; set; } = new();
    }

    public sealed class ItemRequest
    {
        public Guid ComplianceRequirementId { get; set; }
        public int SequenceNo { get; set; }
        public bool IsMandatory { get; set; } = true;
        public bool AppliesPerHorse { get; set; }
        public string? Instruction { get; set; }
    }

    public sealed class ChangeStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
