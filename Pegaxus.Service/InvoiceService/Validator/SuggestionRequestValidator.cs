using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class SuggestionRequestValidator : AbstractValidator<Request.SuggestionRequest>
{
    public SuggestionRequestValidator()
    {
        RuleFor(request => request.Type)
            .Must(type => Enum.GetNames<InvoiceType>().Contains(type?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Type must be QUOTATION, DEPOSIT, FINAL, SUPPLEMENTARY or CREDIT_NOTE.");
        RuleFor(request => request.TransitDays).InclusiveBetween(QuotationCatalog.MinTransitDays, QuotationCatalog.MaxTransitDays)
            .When(request => request.TransitDays.HasValue).WithMessage($"Transit days must be between {QuotationCatalog.MinTransitDays} and {QuotationCatalog.MaxTransitDays}.");
    }
}
