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
