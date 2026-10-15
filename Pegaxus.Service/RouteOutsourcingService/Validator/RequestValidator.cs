using FluentValidation;

namespace Pegaxus.Service.RouteOutsourcingService.Validator;

public sealed class RequestValidator : AbstractValidator<Request>
{
    public RequestValidator()
    {
        RuleFor(request => request.Outsourced).NotNull();
        RuleFor(request => request.LastUpdatedAt).NotEmpty();
        RuleFor(request => request.TransportProviderId).Must(id => id.HasValue && id != Guid.Empty)
            .When(request => request.Outsourced == true).WithErrorCode("PROVIDER_REQUIRED")
            .WithMessage("Please select a valid Transport Provider for this outsourced segment.");
        RuleFor(request => request.TransportProviderId).Null().When(request => request.Outsourced == false);
        RuleFor(request => request.Note).MaximumLength(2000);
    }
}
