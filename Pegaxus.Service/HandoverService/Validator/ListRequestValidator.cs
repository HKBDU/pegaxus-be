using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.HandoverService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThanOrEqualTo(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => (long)request.PageIndex * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(request => request.SortBy).Must(value => value is "sequenceNo:asc" or "sequenceNo:desc")
            .WithMessage("SortBy must be sequenceNo:asc or sequenceNo:desc.");
        RuleFor(request => request.HandoverType).Must(value => ValidCsv<HandoverType>(value)).WithMessage("HandoverType must contain handover type names separated by commas.");
        RuleFor(request => request.Status).Must(value => ValidCsv<HandoverStatus>(value)).WithMessage("Status must contain handover status names separated by commas.");
    }

    private static bool ValidCsv<T>(string? value) where T : struct, Enum => value is null ||
        value.Split(',').All(part => Enum.GetNames<T>().Contains(part.Trim(), StringComparer.OrdinalIgnoreCase));
}
