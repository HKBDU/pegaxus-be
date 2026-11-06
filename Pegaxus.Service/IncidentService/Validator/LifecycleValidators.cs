using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.IncidentService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(item => item.Status).Must(value => value is null || value.Split(',', StringSplitOptions.TrimEntries)
            .All(status => Enum.GetNames<IncidentStatus>().Contains(status))).WithMessage("A valid comma-separated incident status is required.");
        RuleFor(item => item.Category).Must(value => value is null || Enum.GetNames<IncidentCategory>().Contains(value));
        RuleFor(item => item.Severity).Must(value => value is null || Enum.GetNames<IncidentSeverity>().Contains(value));
        RuleFor(item => item.TransportId).NotEqual(Guid.Empty).When(item => item.TransportId.HasValue);
        RuleFor(item => item.Search).MaximumLength(200);
        RuleFor(item => item.OccurredTo).Must((item, to) => item.OccurredFrom is null || to is null || to >= item.OccurredFrom)
            .WithMessage("Occurred to must be on or after occurred from.");
        RuleFor(item => item.PageIndex).GreaterThanOrEqualTo(1);
        RuleFor(item => item.PageSize).InclusiveBetween(1, 100);
        RuleFor(item => item).Must(item => ((long)item.PageIndex - 1) * item.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("PageIndex");
        RuleFor(item => item.SortBy).Must(value => !string.IsNullOrWhiteSpace(value) && value.Split(',', StringSplitOptions.TrimEntries)
            .All(part => part is "occurredAt:asc" or "occurredAt:desc" or "severity:asc" or "severity:desc" or
                "status:asc" or "status:desc" or "updatedAt:asc" or "updatedAt:desc"))
            .WithMessage("Sort must use occurredAt, severity, status or updatedAt with asc or desc.");
    }
}

public sealed class SelfResolveRequestValidator : AbstractValidator<Request.SelfResolveRequest>
{
    public SelfResolveRequestValidator()
    {
        RuleFor(item => item.LastUpdatedAt).NotNull();
        RuleFor(item => item.ResolutionSummary).Must(value => value?.Trim().Length >= 20).MaximumLength(2000)
            .WithMessage("The incident resolution summary is required (at least 20 characters)");
    }
}

public sealed class ResolveRequestValidator : AbstractValidator<Request.ResolveRequest>
{
    public ResolveRequestValidator(IValidator<Request.SelfResolveRequest> selfResolveValidator) => Include(selfResolveValidator);
}

public sealed class CancelRequestValidator : AbstractValidator<Request.CancelRequest>
{
    public CancelRequestValidator()
    {
        RuleFor(item => item.LastUpdatedAt).NotNull();
        RuleFor(item => item.CancellationReason).Must(value => value?.Trim().Length >= 10).MaximumLength(2000)
            .WithMessage("Cancellation reason must contain between 10 and 2000 characters after trimming.");
    }
}

public sealed class RaiseSeverityRequestValidator : AbstractValidator<Request.RaiseSeverityRequest>
{
    public RaiseSeverityRequestValidator()
    {
        RuleFor(item => item.LastUpdatedAt).NotNull();
        RuleFor(item => item.Severity).Must(value => Enum.GetNames<IncidentSeverity>().Contains(value));
        RuleFor(item => item.Reason).Must(value => value?.Trim().Length >= 10).MaximumLength(2000)
            .WithMessage("Reason must contain between 10 and 2000 characters after trimming.");
    }
}
