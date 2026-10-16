using FluentValidation;
using Pegaxus.Service.Common;

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
                request.OriginLatitude.HasValue ||
                request.OriginLongitude.HasValue ||
                request.DestinationLatitude.HasValue ||
                request.DestinationLongitude.HasValue ||
                request.RequestedDepartureDate.HasValue ||
                request.RequestedArrivalDate.HasValue ||
                request.SpecialRequirements is not null ||
                request.CustomerNote is not null)
            .WithMessage("At least one field must be updated.");
        RuleFor(x => x).Must(request => request.OriginLatitude.HasValue == request.OriginLongitude.HasValue)
            .WithMessage("originLatitude and originLongitude must be sent together.");
        RuleFor(x => x).Must(request => request.DestinationLatitude.HasValue == request.DestinationLongitude.HasValue)
            .WithMessage("destinationLatitude and destinationLongitude must be sent together.");
        RuleFor(x => x.OriginAddress).NotEmpty().MaximumLength(500).When(x => x.OriginAddress is not null);
        RuleFor(x => x.OriginCountry)
            .NotEmpty()
            .MaximumLength(100)
            .Must(country => CountryUtils.IsEuCountry(country) || CountryUtils.IsUnitedKingdom(country))
            .WithMessage("Origin country must be an accepted 27 EU member country or United Kingdom.")
            .When(x => x.OriginCountry is not null);
        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(500).When(x => x.DestinationAddress is not null);
        RuleFor(x => x.DestinationCountry)
            .NotEmpty()
            .MaximumLength(100)
            .Must(country => CountryUtils.IsEuCountry(country) || CountryUtils.IsUnitedKingdom(country))
            .WithMessage("Destination country must be an accepted 27 EU member country or United Kingdom.")
            .When(x => x.DestinationCountry is not null);
    }
}
