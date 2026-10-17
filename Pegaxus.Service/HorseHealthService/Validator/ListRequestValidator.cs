using FluentValidation;
using Pegaxus.Repository.Enum;

namespace Pegaxus.Service.HorseHealthService.Validator;

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        RuleFor(request => request.PageIndex).GreaterThanOrEqualTo(0)
            .Must((request, index) => (long)index * request.PageSize <= int.MaxValue)
            .WithMessage("Page index is too large.");
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.LogType).Must(value => value is null || value.Split(',', StringSplitOptions.TrimEntries)
            .All(Enum.GetNames<HorseHealthLogType>().Contains)).WithMessage("Invalid log type.");
        RuleFor(request => request.HealthStatus).Must(value => value is null || value.Split(',', StringSplitOptions.TrimEntries)
            .All(Enum.GetNames<HorseHealthStatus>().Contains)).WithMessage("Invalid health status.");
    }
}
