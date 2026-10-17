using FluentValidation;

namespace Pegaxus.Service.HandoverService.Validator;

public sealed class CancelRequestValidator : AbstractValidator<Request.CancelRequest>
{
    public CancelRequestValidator()
    {
        RuleFor(request => request.LastUpdatedAt).NotNull().NotEqual(default(DateTimeOffset));
        RuleFor(request => request.CancellationReason).Must(value => value?.Trim().Length >= 10)
            .WithMessage("CancellationReason must contain at least 10 characters after trimming.");
    }
}
