using FluentValidation;

namespace Pegaxus.Service.Auth.Validations;

public class VerifyEmailRequestValidator : AbstractValidator<Request.VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Otp).Matches(@"^\d{6}$");
    }
}

public class ResendOtpRequestValidator : AbstractValidator<Request.ResendOtpRequest>
{
    public ResendOtpRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Purpose)
            .Must(purpose => purpose is not null &&
                (purpose.Equals("EMAIL_VERIFICATION", StringComparison.OrdinalIgnoreCase) ||
                 purpose.Equals("PASSWORD_RESET", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Purpose must be EMAIL_VERIFICATION or PASSWORD_RESET.");
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<Request.RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<Request.ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Otp).Matches(@"^\d{6}$");
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .Length(8, 128)
            .Matches(@"[A-Z]")
            .Matches(@"[a-z]")
            .Matches(@"[0-9]");
        RuleFor(x => x.ConfirmPassword)
            .NotEmpty()
            .Equal(x => x.NewPassword).WithMessage("ConfirmPassword doesn't match.");
    }
}
