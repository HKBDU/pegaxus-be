using FluentValidation;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.BookingService.Validations;

public class CreateBookingRequestValidator : AbstractValidator<Request.CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.OriginAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.OriginCountry)
            .NotEmpty()
            .MaximumLength(100)
            .Must(country => CountryUtils.IsEuCountry(country) || CountryUtils.IsUnitedKingdom(country))
            .WithMessage("Origin country must be an accepted 27 EU member country or United Kingdom.");

        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DestinationCountry)
            .NotEmpty()
            .MaximumLength(100)
            .Must(country => CountryUtils.IsEuCountry(country) || CountryUtils.IsUnitedKingdom(country))
            .WithMessage("Destination country must be an accepted 27 EU member country or United Kingdom.");

        RuleFor(x => x)
            .Custom((request, context) =>
            {
                var (isValid, errorMessage) = CountryUtils.ValidateRoute(request.OriginCountry, request.DestinationCountry);
                if (!isValid)
                {
                    context.AddFailure("DestinationCountry", errorMessage);
                }
            });

        RuleForEach(x => x.Horses).SetValidator(new BookingHorseRequestValidator());
    }
}

public class BookingHorseRequestValidator : AbstractValidator<Request.BookingHorseRequest>
{
    public BookingHorseRequestValidator()
    {
        RuleFor(x => x.HorseId).NotEmpty();
        RuleFor(x => x.SpecialHandlingNotes).MaximumLength(2000).When(x => x.SpecialHandlingNotes is not null);
    }
}
