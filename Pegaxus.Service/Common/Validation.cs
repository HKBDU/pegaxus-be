using FluentValidation;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Common;

public static class Validation
{
    /// <summary>Validates the request and throws a 400 <see cref="BadRequestException"/> with all messages joined by a space.</summary>
    public static void Validate<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (!result.IsValid)
            throw new BadRequestException(string.Join(" ", result.Errors.Select(error => error.ErrorMessage)));
    }

    /// <summary>Validates the request and throws a 400 with camel-case field errors; root-level errors use $.</summary>
    public static void ValidateFields<T>(T request, IValidator<T> validator)
    {
        var result = validator.Validate(request);
        if (!result.IsValid)
            throw new FieldValidationException(result.Errors.Select(error => new FieldError(
                string.IsNullOrEmpty(error.PropertyName) ? "$" : char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..],
                error.ErrorMessage)).ToList());
    }
}
