using FluentValidation;

namespace Pegaxus.Service.FinancialReportService.Validator;

public sealed class UpdateNoteRequestValidator : AbstractValidator<Request.UpdateNoteRequest>
{
    public UpdateNoteRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.SummaryNote).MaximumLength(2000);
    }
}
