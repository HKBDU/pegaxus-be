using FluentValidation;

namespace Pegaxus.Service.BookingService.Validations;

public class RejectBookingRequestValidator : AbstractValidator<Request.RejectBookingRequest>
{
    public RejectBookingRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(1000);
    }
}
