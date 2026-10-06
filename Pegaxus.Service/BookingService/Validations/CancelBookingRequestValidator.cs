using FluentValidation;

namespace Pegaxus.Service.BookingService.Validations;

public class CancelBookingRequestValidator : AbstractValidator<Request.CancelBookingRequest>
{
    public CancelBookingRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(1000);
    }
}
