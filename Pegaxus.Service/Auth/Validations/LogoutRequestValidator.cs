using FluentValidation;

namespace Pegaxus.Service.Auth.Validations;

public class LogoutRequestValidator : AbstractValidator<Request.LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessage("RefreshToken is required.");
    }
}
