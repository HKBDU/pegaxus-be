using FluentValidation;
using Pegaxus.Service.Common;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.Service.Test;

public class ValidationTests
{
    [Fact]
    public void ValidateFields_PreservesErrorContractForRootRules()
    {
        var validator = new InlineValidator<string>();
        validator.RuleFor(value => value).NotEmpty();

        var error = Assert.Throws<FieldValidationException>(() => Validation.ValidateFields("", validator));

        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Equal("The input data is invalid", error.Message);
        Assert.Equal("$", Assert.Single(error.Errors).Field);
    }
}
