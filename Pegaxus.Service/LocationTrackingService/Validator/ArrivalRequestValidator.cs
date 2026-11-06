using FluentValidation;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.LocationTrackingService.Validator;

public sealed class ArrivalRequestValidator : AbstractValidator<Request.ArrivalRequest>
{
    public ArrivalRequestValidator()
    {
        RuleFor(request => request.Latitude).NotNull().Measurement(Limits.Latitude);
        RuleFor(request => request.Longitude).NotNull().Measurement(Limits.Longitude);
    }
}
