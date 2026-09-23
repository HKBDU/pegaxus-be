using FluentValidation;

namespace Pegaxus.Service.BookingService.Validations;

public class UpdateBookingRequestValidator : AbstractValidator<Request.UpdateBookingRequest>
{
    public UpdateBookingRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotEmpty();
        RuleFor(x => x).Must(request =>
                request.OriginAddress is not null ||
                request.OriginCountry is not null ||
                request.DestinationAddress is not null ||
                request.DestinationCountry is not null ||
                request.RequestedDepartureDate.HasValue ||
                request.RequestedArrivalDate.HasValue ||
                request.SpecialRequirements is not null ||
                request.CustomerNote is not null)
            .WithMessage("At least one field must be updated.");
        RuleFor(x => x.OriginAddress).NotEmpty().MaximumLength(500).When(x => x.OriginAddress is not null);
        RuleFor(x => x.OriginCountry).NotEmpty().MaximumLength(100).When(x => x.OriginCountry is not null);
        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(500).When(x => x.DestinationAddress is not null);
        RuleFor(x => x.DestinationCountry).NotEmpty().MaximumLength(100).When(x => x.DestinationCountry is not null);
    }
}
