using FluentValidation;

namespace Pegaxus.Service.PersonnelAssignmentService.Validator;

public sealed class AssignRequestValidator : AbstractValidator<Request.AssignRequest>
{
    public AssignRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Segments).NotNull();
        RuleFor(x => x.Segments).Must(x => x is null || x.Count <= 1000 &&
            x.All(s => s is not null) && x.Select(s => s.RoutePlanRouteId).Distinct().Count() == x.Count)
            .WithMessage("Provide at most 1000 distinct segments.");
        RuleForEach(x => x.Segments).SetValidator(new SegmentRequestValidator());
    }
}

public sealed class SegmentRequestValidator : AbstractValidator<Request.SegmentRequest>
{
    public SegmentRequestValidator()
    {
        RuleFor(x => x.RoutePlanRouteId).NotEmpty();
        RuleFor(x => x.DriverEmployeeId).NotNull().NotEqual(Guid.Empty);
        RuleFor(x => x.EscortEmployeeId).NotNull().NotEqual(Guid.Empty);
        RuleFor(x => x).Must(x => x.DriverEmployeeId != x.EscortEmployeeId)
            .WithMessage("Driver and Escort must be different employees.");
    }
}

public sealed class ReplaceRequestValidator : AbstractValidator<Request.ReplaceRequest>
{
    public ReplaceRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.DriverEmployeeId).NotEqual(Guid.Empty).When(x => x.DriverEmployeeId.HasValue);
        RuleFor(x => x.EscortEmployeeId).NotEqual(Guid.Empty).When(x => x.EscortEmployeeId.HasValue);
        RuleFor(x => x).Must(x => x.DriverEmployeeId.HasValue || x.EscortEmployeeId.HasValue)
            .WithMessage("Select a replacement Driver or Escort.");
    }
}
