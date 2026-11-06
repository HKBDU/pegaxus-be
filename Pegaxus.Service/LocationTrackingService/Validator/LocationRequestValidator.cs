using FluentValidation;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.LocationTrackingService.Validator;

public sealed class LocationRequestValidator : AbstractValidator<Request.LocationRequest>
{
    public LocationRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.Latitude).NotNull().Measurement(Limits.Latitude);
        RuleFor(request => request.Longitude).NotNull().Measurement(Limits.Longitude);
        RuleFor(request => request).Must(request => !(request.Latitude == 0 && request.Longitude == 0))
            .WithMessage("Latitude and longitude must not both be 0.").OverridePropertyName("latitude");
        RuleFor(request => request.RecordedAt).NotNull().Must(value => value is null || value != default(DateTimeOffset) && value <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("Recorded at is required and must not be more than 5 minutes in the future.");
        RuleFor(request => request.SpeedKmh).Measurement(Limits.TrackingSpeedKmh);
        RuleFor(request => request.HeadingDegrees).Measurement(Limits.HeadingDegrees);
        RuleFor(request => request.AccuracyMeters).Measurement(Limits.TrackingAccuracyMeters);
        RuleFor(request => request.RoutePlanRouteId).NotEqual(Guid.Empty);
    }
}
