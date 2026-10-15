using Pegaxus.Service.Auth.Validations;

using AuthRequest = Pegaxus.Service.Auth.Request;

namespace Pegaxus.Service.Test;

public class AuthValidationTests
{
    [Fact]
    public void Register_RejectsWeakAndMismatchedPassword()
    {
        var result = new RegisterRequestValidator().Validate(new AuthRequest.RegisterRequest
        {
            FullName = "Test User",
            Email = "test@example.com",
            Password = "weak",
            ConfirmPassword = "different"
        });

        Assert.Contains(result.Errors, error => error.PropertyName == "Password");
        Assert.Contains(result.Errors, error => error.PropertyName == "ConfirmPassword");
    }

    [Fact]
    public void ForgotPassword_RejectsInvalidEmail()
    {
        var result = new ForgotPasswordRequestValidator().Validate(
            new AuthRequest.ForgotPasswordRequest { Email = "invalid" });

        Assert.Contains(result.Errors, error => error.PropertyName == "Email");
    }
}
