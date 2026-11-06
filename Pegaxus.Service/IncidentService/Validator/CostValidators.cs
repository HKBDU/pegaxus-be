using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.IncidentService.Validator;

public sealed class CreateCostRequestValidator : AbstractValidator<Request.CreateCostRequest>
{
    public CreateCostRequestValidator()
    {
        RuleFor(item => item.Category).Must(value => Enum.GetNames<EmergencyCostCategory>().Contains(value));
        RuleFor(item => item.RequestedAmount).NotNull().Must(value => value is null || Vnd.IsWhole(value.Value))
            .WithMessage("Amount must be a whole number of VND");
        RuleFor(item => item.RequestedAmount).GreaterThan(0).LessThanOrEqualTo(9_999_999_999_999_999m);
        RuleFor(item => item.Description).Must(value => value?.Trim().Length >= 10).MaximumLength(2000);
        RuleFor(item => item.Justification).Must(value => value?.Trim().Length >= 10).MaximumLength(2000);
        RuleFor(item => item.PhotoFiles).Must(files => files is { Count: <= 5 } && files.All(file => file is not null &&
                !string.IsNullOrWhiteSpace(file.PublicId) && file.PublicId.Length <= 255) &&
                files.Select(file => file.PublicId).Distinct(StringComparer.Ordinal).Count() == files.Count)
            .WithMessage("At most 5 distinct photos with non-empty publicIds of at most 255 characters are accepted.");
    }
}

public sealed class CostDecisionRequestValidator : AbstractValidator<Request.CostDecisionRequest>
{
    public CostDecisionRequestValidator()
    {
        RuleFor(item => item.LastUpdatedAt).NotNull();
        RuleFor(item => item.Decision).Must(value => value is "APPROVE" or "REJECT");
        RuleFor(item => item.ApprovedAmount).Must(value => value is null || Vnd.IsWhole(value.Value))
            .When(item => item.Decision == "APPROVE").WithMessage("Amount must be a whole number of VND");
        RuleFor(item => item.DecisionReason).MaximumLength(2000);
        RuleFor(item => item.DecisionReason).Must(value => value?.Trim().Length >= 10)
            .When(item => item.Decision == "REJECT").WithMessage("Decision reason must contain at least 10 characters after trimming.");
    }
}

public sealed class CostListRequestValidator : AbstractValidator<Request.CostListRequest>
{
    public CostListRequestValidator()
    {
        RuleFor(item => item.Status).Must(value => value is null || value.Split(',', StringSplitOptions.TrimEntries)
            .All(status => Enum.GetNames<EmergencyCostStatus>().Contains(status)));
        RuleFor(item => item.Category).Must(value => value is null || Enum.GetNames<EmergencyCostCategory>().Contains(value));
        RuleFor(item => item.IncidentReportId).NotEqual(Guid.Empty).When(item => item.IncidentReportId.HasValue);
        RuleFor(item => item.TransportId).NotEqual(Guid.Empty).When(item => item.TransportId.HasValue);
        RuleFor(item => item.Keyword).MaximumLength(200);
        RuleFor(item => item.ToDate).Must((item, to) => item.FromDate is null || to is null || to >= item.FromDate);
        RuleFor(item => item.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(item => item.PageSize).InclusiveBetween(1, 100);
        RuleFor(item => item).Must(item => ((long)item.PageIndex - 1) * item.PageSize <= int.MaxValue)
            .OverridePropertyName("PageIndex");
        RuleFor(item => item.SortBy).Must(value => !string.IsNullOrWhiteSpace(value) && value.Split(',', StringSplitOptions.TrimEntries)
            .All(part => part is "requestedAt:asc" or "requestedAt:desc" or "updatedAt:asc" or "updatedAt:desc" or
                "requestedAmount:asc" or "requestedAmount:desc" or "status:asc" or "status:desc"));
    }
}
