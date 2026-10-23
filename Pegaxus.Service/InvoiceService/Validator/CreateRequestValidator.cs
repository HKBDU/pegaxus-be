using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class CreateRequestValidator : AbstractValidator<Request.CreateRequest>
{
    public CreateRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.Type)
            .Must(type => Enum.GetNames<InvoiceType>().Contains(type?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be QUOTATION, DEPOSIT, FINAL, SUPPLEMENTARY or CREDIT_NOTE.");
        RuleFor(request => request.DueDate).NotNull()
            .Must(date => !date.HasValue || date.Value >= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            .WithMessage("Due date must not be before today.");
        RuleFor(request => request.DueDate)
            .Must(date => !date.HasValue || date.Value <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddYears(Limits.DateWindowYears))
            .WithMessage($"Due date must be within {Limits.DateWindowYears} years from today.");
        RuleFor(request => request.TransitDays).InclusiveBetween(QuotationCatalog.MinTransitDays, QuotationCatalog.MaxTransitDays)
            .When(request => request.TransitDays.HasValue).WithMessage($"Transit days must be between {QuotationCatalog.MinTransitDays} and {QuotationCatalog.MaxTransitDays}.");
        RuleFor(request => request.Items).NotNull().WithMessage("Items are required; an empty array is allowed.")
            .When(request => MustSendItems(request.Type));
        RuleForEach(request => request.Items).NotNull().SetValidator(new ItemValidator());
    }

    // The server builds the lines of QUOTATION, DEPOSIT and FINAL, so only the other types must send items.
    private static bool MustSendItems(string? type) =>
        !(Enum.TryParse<InvoiceType>(type?.Trim(), true, out var parsed) && parsed is InvoiceType.QUOTATION or InvoiceType.DEPOSIT or InvoiceType.FINAL);

    private sealed class ItemValidator : AbstractValidator<Request.ItemRequest>
    {
        public ItemValidator()
        {
            When(item => !item.IsDepositDeduction, () =>
            {
                RuleFor(item => item.ItemType)
                    .Must(type => Enum.GetNames<InvoiceItemType>().Contains(type?.Trim(), StringComparer.OrdinalIgnoreCase))
                    .WithMessage("Invalid invoice item type.");
                RuleFor(item => item.Description).NotEmpty().MaximumLength(500);
                RuleFor(item => item.Quantity).GreaterThan(0).WithMessage("Quantity and unit price must be greater than 0")
                    .LessThanOrEqualTo(Limits.InvoiceQuantityMax).WithMessage($"Quantity must not exceed {Limits.InvoiceQuantityMax}.")
                    .Must(Vnd.IsWhole).WithMessage("Quantity must be a whole number");
                RuleFor(item => item.Price).GreaterThan(0).WithMessage("Quantity and unit price must be greater than 0")
                    .LessThanOrEqualTo(9_999_999_999_999_999m).Must(Vnd.IsWhole).WithMessage("Amount must be a whole number of VND");
            });
        }
    }
}
