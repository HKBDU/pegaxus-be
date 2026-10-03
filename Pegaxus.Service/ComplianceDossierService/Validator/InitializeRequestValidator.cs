using FluentValidation;

namespace Pegaxus.Service.ComplianceDossierService.Validator;

public sealed class InitializeRequestValidator : AbstractValidator<Request.InitializeRequest>
{
    public InitializeRequestValidator()
    {
        RuleFor(request => request.DossierTemplateId).Must(id => id != Guid.Empty)
            .WithMessage("DossierTemplateId must be a valid identifier.");
        RuleFor(request => request.Items).NotEmpty()
            .When(request => request.DossierTemplateId is null || request.Items is not null)
            .WithMessage("Select at least one ComplianceRequirement.");
        RuleForEach(request => request.Items).NotNull().ChildRules(item =>
        {
            item.RuleFor(value => value.ComplianceRequirementId).NotEmpty();
        });
        RuleFor(request => request.Items)
            .Must(items => items is null || items.Select(item => item.ComplianceRequirementId).Distinct().Count() == items.Count)
            .When(request => request.Items is not null && request.Items.All(item => item is not null))
            .WithMessage("Each ComplianceRequirement must be unique in the selection.");
    }
}
