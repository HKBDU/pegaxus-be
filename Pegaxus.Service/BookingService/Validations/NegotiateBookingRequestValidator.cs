using FluentValidation;

namespace Pegaxus.Service.BookingService.Validations;

public sealed class NegotiateBookingRequestValidator : AbstractValidator<Request.NegotiateBookingRequest>
{
    public NegotiateBookingRequestValidator()
    {
        RuleFor(request => request.Status).Must(value => value?.Trim().ToUpperInvariant() is "DRAFT" or "CANCELLED")
            .WithMessage("Negotiated status must be DRAFT or CANCELLED.");
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(2000);
    }
}
