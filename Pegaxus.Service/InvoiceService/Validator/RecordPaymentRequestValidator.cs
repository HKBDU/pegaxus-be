using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class RecordPaymentRequestValidator : AbstractValidator<Request.RecordPaymentRequest>
{
    public RecordPaymentRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).NotNull().GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.Currency).Must(x => new[] { "EUR", "VND" }.Contains(x?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("currency must be EUR or VND.");
        RuleFor(x => x.PaymentMethod).Must(x => Enum.GetNames<PaymentMethod>().Contains(x?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("paymentMethod must be CASH, BANK_TRANSFER or OTHER.");
        RuleFor(x => x.TransactionReference).MaximumLength(150);
        RuleFor(x => x.PaidAt).Must(x => !x.HasValue || x.Value <= clock.GetUtcNow())
            .WithMessage("paidAt must not be in the future.");
    }
}
