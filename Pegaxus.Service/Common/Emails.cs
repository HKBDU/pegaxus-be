using FluentValidation;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

/// <summary>Same email rule as the FluentValidation validators (<c>EmailAddress()</c>, 255 characters), for services that validate by throwing.</summary>
public static class Emails
{
    private static readonly InlineValidator<string> Rule = new() { validator => validator.RuleFor(value => value).NotEmpty().EmailAddress().MaximumLength(255) };

    public static void EnsureValid(string? value)
    {
        if (!Rule.Validate(value ?? string.Empty).IsValid)
            throw new BadRequestException("Email is invalid", "INVALID_EMAIL");
    }
}
