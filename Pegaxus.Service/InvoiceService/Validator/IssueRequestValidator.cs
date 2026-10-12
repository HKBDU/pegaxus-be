using FluentValidation;

namespace Pegaxus.Service.InvoiceService.Validator;

public sealed class IssueRequestValidator : AbstractValidator<Request.IssueRequest>
{
    public IssueRequestValidator()
    {
        RuleFor(x => x.LastUpdatedAt).NotNull().Must(x => !x.HasValue || x.Value != default)
            .WithMessage("lastUpdatedAt is required.");
    }
}
