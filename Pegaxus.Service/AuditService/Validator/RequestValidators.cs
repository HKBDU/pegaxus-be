using FluentValidation;

namespace Pegaxus.Service.AuditService.Validator;

internal static class AuditRules
{
    public static readonly string[] Blocks = ["booking", "horses", "routing", "compliance", "operations", "finance"];
}

public sealed class ArchiveRequestValidator : AbstractValidator<Request.ArchiveRequest>
{
    public ArchiveRequestValidator()
    {
        RuleFor(request => request.Search).MaximumLength(200);
        RuleFor(request => request.OriginCountry).MaximumLength(100);
        RuleFor(request => request.DestinationCountry).MaximumLength(100);
        RuleFor(request => request.CompletedTo).Must((request, end) => !end.HasValue || !request.CompletedFrom.HasValue || end >= request.CompletedFrom)
            .WithMessage("completedTo must not be earlier than completedFrom.");
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => ((long)request.PageIndex - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("pageIndex");
        RuleFor(request => request.SortBy).Must(sort => new[] { "completedat:asc", "completedat:desc", "transportcode:asc", "transportcode:desc" }.Contains(sort?.Trim().ToLowerInvariant()))
            .WithMessage("Sort must use completedAt or transportCode with :asc or :desc.");
    }
}

public sealed class AuditRequestValidator : AbstractValidator<Request.AuditRequest>
{
    public AuditRequestValidator()
    {
        RuleFor(request => request.Blocks).Must(value => string.IsNullOrWhiteSpace(value) ||
                value.Split(',', StringSplitOptions.TrimEntries).All(part => AuditRules.Blocks.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"blocks must be a comma-separated list of {string.Join(", ", AuditRules.Blocks)}.");
        RuleFor(request => request.Limit).InclusiveBetween(1, 1000);
    }
}

public sealed class ExportRequestValidator : AbstractValidator<Request.ExportRequest>
{
    public ExportRequestValidator()
    {
        RuleFor(request => request.Format).Must(value => value is not null && new[] { "ZIP", "PDF" }.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("format must be ZIP or PDF.");
        RuleFor(request => request.Mode).Must(value => value is not null && new[] { "STANDARD", "INSURANCE" }.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("mode must be STANDARD or INSURANCE.");
    }
}
