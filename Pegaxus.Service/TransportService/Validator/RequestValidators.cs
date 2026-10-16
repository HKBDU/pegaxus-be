using FluentValidation;

namespace Pegaxus.Service.TransportService.Validator;

public sealed class AssignedRequestValidator : AbstractValidator<Request.AssignedRequest>
{
    public AssignedRequestValidator()
    {
        RuleFor(request => request.Status).Must(value => value is null ||
            value.Split(',', StringSplitOptions.TrimEntries).All(status => status is "READY" or "IN_TRANSIT" or "COMPLETED"))
            .WithMessage("Status must contain only READY, IN_TRANSIT or COMPLETED.");
    }
}

public sealed class ListRequestValidator : AbstractValidator<Request.ListRequest>
{
    public ListRequestValidator()
    {
        Include(new AssignedRequestValidator());
        RuleFor(request => request.PageIndex).GreaterThanOrEqualTo(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request).Must(request => (long)request.PageIndex * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.").OverridePropertyName("PageIndex");
        RuleFor(request => request.SortBy).Must(value => value is "departureAt:asc" or "departureAt:desc" or "transportCode:asc")
            .WithMessage("Sort must be departureAt:asc, departureAt:desc or transportCode:asc.");
    }
}
