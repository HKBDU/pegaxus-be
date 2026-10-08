using FluentValidation;
using Pegaxus.Repository.Enum;
using System.Globalization;

namespace Pegaxus.Service.RouteChangeService.Validator;

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator()
    {
        RuleFor(x => x.ClientRequestId).NotEqual(Guid.Empty).When(x => x.ClientRequestId.HasValue);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(4000);
        RuleFor(x => x).Must(x => x.BackupRoutePlanId.HasValue != (x.ReplacementSegments is not null))
            .WithMessage("Provide exactly one of backupRoutePlanId or replacementSegments.");
        RuleFor(x => x.BackupRoutePlanId).NotEqual(Guid.Empty).When(x => x.BackupRoutePlanId.HasValue);
        RuleFor(x => x.ReplacementSegments).NotEmpty().Must(x => x!.Count <= 100).When(x => x.ReplacementSegments is not null);
        RuleForEach(x => x.ReplacementSegments).NotNull().SetValidator(new SegmentRequestValidator());
        RuleFor(x => x.NewEstimatedArrivalAt).NotEmpty();
        RuleFor(x => x.EstimatedExtraCost).InclusiveBetween(0, 9999999999999999.99m)
            .Must(x => x == decimal.Round(x!.Value, 2)).When(x => x.EstimatedExtraCost.HasValue);
        RuleFor(x => x.Currency).Must(IsCurrency).When(x => x.EstimatedExtraCost.HasValue || x.Currency is not null)
            .WithMessage("Currency must be an uppercase ISO 4217 code.");
        RuleFor(x => x.Note).MaximumLength(4000);
    }

    private static bool IsCurrency(string? code) => code is { Length: 3 } &&
        CultureInfo.GetCultures(CultureTypes.SpecificCultures).Any(c => new RegionInfo(c.Name).ISOCurrencySymbol == code);
}

public sealed class SegmentRequestValidator : AbstractValidator<Request.SegmentRequest>
{
    public SegmentRequestValidator()
    {
        RuleFor(x => x.SequenceNo).GreaterThan(0);
        RuleFor(x => x).Must(x => x.RouteId is { } id ? id != Guid.Empty &&
            x.FromLocationId is null && x.ToLocationId is null && x.TransportMode is null :
            x.FromLocationId is { } from && from != Guid.Empty && x.ToLocationId is { } to && to != Guid.Empty && from != to &&
            Enum.GetNames<TransportMode>().Contains(x.TransportMode))
            .WithMessage("Provide routeId or valid fromLocationId, toLocationId and transportMode.");
        RuleFor(x => x.PlannedDepartureAt).NotEmpty();
        RuleFor(x => x.PlannedArrivalAt).GreaterThan(x => x.PlannedDepartureAt);
        RuleFor(x => x.VehicleIds).Must(x => x is null || x.Count <= 100 && x.All(id => id != Guid.Empty) && x.Distinct().Count() == x.Count);
        RuleFor(x => x).Must(x => x.Outsourcing is null || x.VehicleIds is null || x.VehicleIds.Count == 0)
            .WithMessage("Outsourced segments cannot use internal vehicles.");
        When(x => x.Outsourcing is not null, () =>
        {
            RuleFor(x => x.Outsourcing!.TransportProviderId).NotEmpty();
            RuleFor(x => x.Outsourcing!.ServiceType).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Outsourcing!.ProviderReference).MaximumLength(150);
            RuleFor(x => x.Outsourcing!.Note).MaximumLength(4000);
        });
        RuleFor(x => x.Note).MaximumLength(4000);
    }
}

public sealed class DecisionRequestValidator : AbstractValidator<Request.DecisionRequest>
{
    public DecisionRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x.Decision).Must(x => x is "APPROVE" or "REJECT" or "EMERGENCY_RETURN");
        RuleFor(x => x.Note).Must(x => x?.Trim().Length >= 10).When(x => x.Decision is "REJECT" or "EMERGENCY_RETURN");
        RuleFor(x => x.Note).MaximumLength(4000);
        RuleFor(x => x).Must(x => x.ReturnSegments is null || x.Decision == "EMERGENCY_RETURN")
            .WithMessage("returnSegments is only available for EMERGENCY_RETURN.");
        RuleFor(x => x.ReturnSegments).NotEmpty().Must(x => x!.Count <= 100).When(x => x.ReturnSegments is not null);
        RuleForEach(x => x.ReturnSegments).NotNull().SetValidator(new SegmentRequestValidator());
    }
}

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(x => x.PageIndex).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => (long)(x.PageIndex - 1) * x.PageSize <= int.MaxValue);
        RuleFor(x => x.Status).Must(x => x is null or "PENDING" or "APPROVED" or "REJECTED" or "EMERGENCY_RETURN");
        RuleFor(x => x.TransportPlanId).NotEqual(Guid.Empty).When(x => x.TransportPlanId.HasValue);
        RuleFor(x => x.SortBy).Must(x => x is "createdAt:asc" or "createdAt:desc");
    }
}
