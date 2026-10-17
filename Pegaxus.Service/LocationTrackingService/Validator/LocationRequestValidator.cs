using FluentValidation;

namespace Pegaxus.Service.LocationTrackingService.Validator;

public sealed class LocationRequestValidator : AbstractValidator<Request.LocationRequest>
{
    public LocationRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.Latitude).NotNull().InclusiveBetween(-90, 90);
        RuleFor(request => request.Longitude).NotNull().InclusiveBetween(-180, 180);
        RuleFor(request => request.RecordedAt).NotNull().Must(value => value is null || value != default(DateTimeOffset) && value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Recorded at is required and must not be more than 5 minutes in the future.");
        RuleFor(request => request.SpeedKmh).GreaterThanOrEqualTo(0);
        RuleFor(request => request.HeadingDegrees).InclusiveBetween(0, 359);
        RuleFor(request => request.AccuracyMeters).GreaterThanOrEqualTo(0);
        RuleFor(request => request.RoutePlanRouteId).NotEqual(Guid.Empty);
    }
}
