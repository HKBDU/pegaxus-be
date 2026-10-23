using FluentValidation;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Common;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class UpdateRequestValidator : AbstractValidator<Request.UpdateRequest>
{
    public UpdateRequestValidator(TimeProvider clock)
    {
        RuleFor(request => request.LastUpdatedAt).NotNull();
        RuleFor(request => request.DueDate).NotNull()
            .Must(date => !date.HasValue || date.Value >= clock.GetUtcNow())
            .WithMessage("Due date must not be in the past.");
        RuleFor(request => request.DueDate)
            .Must(date => !date.HasValue || date.Value <= clock.GetUtcNow().AddYears(Limits.DateWindowYears))
            .WithMessage($"Due date must be within {Limits.DateWindowYears} years from today.");
        RuleFor(request => request.TransitDays).InclusiveBetween(QuotationCatalog.MinTransitDays, QuotationCatalog.MaxTransitDays)
            .When(request => request.TransitDays.HasValue).WithMessage($"Transit days must be between {QuotationCatalog.MinTransitDays} and {QuotationCatalog.MaxTransitDays}.");
        RuleForEach(request => request.Items).NotNull().SetValidator(new ItemValidator());
        RuleFor(request => request.Items).Must(items => items is null || items.Where(item => item is not null && !item.IsDepositDeduction)
            .Where(item => item.InvoiceItemId.HasValue).GroupBy(item => item.InvoiceItemId).All(group => group.Count() == 1))
            .WithMessage("Invoice item identifiers must be distinct.");
    }

    private sealed class ItemValidator : AbstractValidator<Request.ItemRequest>
    {
        public ItemValidator()
        {
            When(item => !item.IsDepositDeduction, () =>
            {
                RuleFor(item => item.InvoiceItemId).NotEqual(Guid.Empty).When(item => item.InvoiceItemId.HasValue);
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
