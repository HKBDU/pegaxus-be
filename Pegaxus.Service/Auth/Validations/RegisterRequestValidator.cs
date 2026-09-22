using FluentValidation;

namespace Pegaxus.Service.Auth.Validations;

public class RegisterRequestValidator : AbstractValidator<Request.RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("FullName is required.")
            .MaximumLength(150).WithMessage("FullName must not exceed 150 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Length(6, 128).WithMessage("Password must be between 6 and 128 characters.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least 1 uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least 1 lowercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least 1 numeric character.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("ConfirmPassword is required.")
            .Equal(x => x.Password).WithMessage("ConfirmPassword doesn't match.");
    }
}
