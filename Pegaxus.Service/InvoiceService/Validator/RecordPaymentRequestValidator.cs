using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class RecordPaymentRequestValidator : AbstractValidator<Request.RecordPaymentRequest>
{
    public RecordPaymentRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Amount).NotNull().GreaterThan(0).LessThanOrEqualTo(9_999_999_999_999_999m)
            .Must(amount => !amount.HasValue || Vnd.IsWhole(amount.Value)).WithMessage("Amount must be a whole number of VND");
        RuleFor(x => x.PaymentMethod).Must(x => Enum.GetNames<PaymentMethod>().Contains(x?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("paymentMethod must be CASH, BANK_TRANSFER or OTHER.");
        RuleFor(x => x.TransactionReference).MaximumLength(150);
        RuleFor(x => x.PaidAt).Must(x => !x.HasValue || x.Value <= clock.GetUtcNow())
            .WithMessage("paidAt must not be in the future.");
    }
}
