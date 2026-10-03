using FluentValidation;

namespace Pegaxus.Service.DossierTemplateService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.PageIndex).Must((request, index) => ((long)index - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(request => request.Status).Must(ChangeStatusRequestValidator.IsValidStatus)
            .When(request => request.Status is not null)
            .WithMessage("Status must be DRAFT, ACTIVE or INACTIVE.");
    }
}

public sealed class SaveRequestValidator : AbstractValidator<Request.SaveRequest>
{
    public SaveRequestValidator()
    {
        RuleFor(request => request.TemplateCode).NotEmpty()
            .Must(value => value is null || value.Trim().Length <= 50)
            .WithMessage("TemplateCode must not exceed 50 characters.");
        RuleFor(request => request.TemplateName).NotEmpty()
            .Must(value => value is null || value.Trim().Length <= 200)
            .WithMessage("TemplateName must not exceed 200 characters.");
        RuleFor(request => request.Status).Must(ChangeStatusRequestValidator.IsValidStatus)
            .WithMessage("Status must be DRAFT, ACTIVE or INACTIVE.");
        RuleFor(request => request.EffectiveTo).Must((request, value) => !(request.EffectiveFrom > value))
            .WithMessage("EffectiveTo must be on or after EffectiveFrom.");
        RuleFor(request => request.Items).NotEmpty()
            .WithMessage("DossierTemplate must contain at least one DossierTemplateItem.");
        RuleForEach(request => request.Items).NotNull().ChildRules(item =>
        {
            item.RuleFor(value => value.ComplianceRequirementId).NotEmpty();
            item.RuleFor(value => value.SequenceNo).GreaterThan(0);
        });
        RuleFor(request => request.Items)
            .Must(items => items.Select(item => item.ComplianceRequirementId).Distinct().Count() == items.Count &&
                items.Select(item => item.SequenceNo).Distinct().Count() == items.Count)
            .When(request => request.Items is not null && request.Items.All(item => item is not null))
            .WithMessage("Duplicate ComplianceRequirement or Sequence Number detected in the same template. Each requirement and sequence number must be unique within a DossierTemplate.")
            .WithErrorCode("DUPLICATE_TEMPLATE_ITEM");
    }
}

public sealed class ChangeStatusRequestValidator : AbstractValidator<Request.ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        RuleFor(request => request.Status).Must(IsValidStatus)
            .WithMessage("Status must be DRAFT, ACTIVE or INACTIVE.");
    }

    internal static bool IsValidStatus(string? value) => value?.Trim().ToUpperInvariant() is "DRAFT" or "ACTIVE" or "INACTIVE";
}
