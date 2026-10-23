using FluentValidation;

namespace Pegaxus.Service.IncidentService.Validator;

public sealed class RerouteEscalationRequestValidator : AbstractValidator<Request.RerouteEscalationRequest>
{
    public RerouteEscalationRequestValidator()
    {
        RuleFor(item => item.Reason).Must(value => value?.Trim().Length >= 10).MaximumLength(2000);
        RuleFor(item => item.SuggestedAction).Must(value => value is null or "HOLD" or "RETURN");
        RuleFor(item => item.SuggestedHoldMinutes).InclusiveBetween(15, 240).When(item => item.SuggestedHoldMinutes.HasValue);
        RuleFor(item => item.SuggestedHoldMinutes).Null().When(item => item.SuggestedAction != "HOLD")
            .WithMessage("Suggested hold minutes require suggestedAction HOLD.");
        RuleFor(item => item.BlockedRoutePlanIds).NotNull().Must(ids => ids is not null && !ids.Contains(Guid.Empty) && ids.Distinct().Count() == ids.Count)
            .WithMessage("Blocked route plans must contain distinct, non-empty UUIDs.");
    }
}

public sealed class RouteHoldRequestValidator : AbstractValidator<Request.RouteHoldRequest>
{
    public RouteHoldRequestValidator()
    {
        RuleFor(item => item.HoldMinutes).InclusiveBetween(15, 240);
        RuleFor(item => item.Reason).Must(value => value?.Trim().Length >= 10).MaximumLength(2000);
        RuleFor(item => item.MonitorIntervalMinutes).InclusiveBetween(10, 120);
        RuleFor(item => item.SafeStopLocationId).NotEqual(Guid.Empty).When(item => item.SafeStopLocationId.HasValue);
    }
}
