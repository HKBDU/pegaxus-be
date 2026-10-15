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
}
