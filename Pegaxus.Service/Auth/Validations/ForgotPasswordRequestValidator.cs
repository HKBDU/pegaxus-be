using FluentValidation;

namespace Pegaxus.Service.Auth.Validations;

public class ForgotPasswordRequestValidator : AbstractValidator<Request.ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters.");
    }
}
