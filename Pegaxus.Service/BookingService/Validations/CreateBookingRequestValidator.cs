using FluentValidation;

namespace Pegaxus.Service.BookingService.Validations;

public class CreateBookingRequestValidator : AbstractValidator<Request.CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.OriginAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.OriginCountry).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DestinationAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DestinationCountry).NotEmpty().MaximumLength(100);
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
