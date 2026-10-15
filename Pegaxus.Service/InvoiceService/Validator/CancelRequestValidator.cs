using FluentValidation;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class CancelRequestValidator : AbstractValidator<Request.CancelRequest>
{
    public CancelRequestValidator()
    {
        RuleFor(x => x.CancellationReason).Must(x => x is not null && x.Trim().Length >= 10)
            .WithMessage("cancellationReason must contain at least 10 characters after trimming.");
    }
}
