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
    }
}
