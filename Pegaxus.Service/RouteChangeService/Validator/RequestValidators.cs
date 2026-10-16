using System.Globalization;
using FluentValidation;

namespace Pegaxus.Service.RouteChangeService.Validator;

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator()
    {
        RuleFor(x => x.Reason).Must(x => x?.Trim().Length >= 10).MaximumLength(4000);
        RuleFor(x => x.NewEstimatedArrivalAt).NotEmpty();
        RuleFor(x => x.EstimatedExtraCost).InclusiveBetween(0, 9999999999999999m)
            .Must(x => Pegaxus.Service.Common.Vnd.IsWhole(x!.Value)).WithMessage("Amount must be a whole number of VND")
            .When(x => x.EstimatedExtraCost.HasValue);
        RuleFor(x => x.Note).MaximumLength(4000);
    }
}

public sealed class DecisionRequestValidator : AbstractValidator<Request.DecisionRequest>
{
    public DecisionRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Decision).Must(x => x is "APPROVE" or "REJECT");
        RuleFor(x => x.Note).Must(x => x?.Trim().Length >= 10).When(x => x.Decision == "REJECT");
        RuleFor(x => x.Note).MaximumLength(4000);
    }
}

public sealed class ActivationRequestValidator : AbstractValidator<Request.ActivationRequest>
{
    public ActivationRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(4000);
        RuleFor(x => x.Segments).Must(x => x is null || x.Count <= 100 && x.All(s => s is not null) &&
            x.Select(s => s.RoutePlanRouteId).Distinct().Count() == x.Count);
        RuleForEach(x => x.Segments).SetValidator(new SegmentRequestValidator());
    }
}

public sealed class SegmentRequestValidator : AbstractValidator<Request.SegmentRequest>
{
    public SegmentRequestValidator()
    {
        RuleFor(x => x.RoutePlanRouteId).NotEmpty();
        RuleFor(x => x.VehicleIds).Must(x => x is null || x.Count <= 100 && x.All(id => id != Guid.Empty) && x.Distinct().Count() == x.Count);
        When(x => x.Outsourcing is not null, () =>
        {
            RuleFor(x => x.Outsourcing!.TransportProviderId).NotEmpty();
            RuleFor(x => x.Outsourcing!.Note).MaximumLength(4000);
        });
    }
}
