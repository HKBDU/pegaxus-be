using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Pegaxus.API.Extensions;
using Pegaxus.API.Models;

namespace Pegaxus.API.Controllers;

/// <summary>Shared response and identity helpers for API controllers.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated account identifier.</summary>
    protected Guid AccountId => User.GetAccountId();

    /// <summary>200 OK wrapped in the standard success envelope.</summary>
    protected OkObjectResult Success(object? value, string message) =>
        Ok(ResponseBuilder.SuccessResponse(value, message, traceId: HttpContext.TraceIdentifier));

    /// <summary>201 Created wrapped in the standard success envelope.</summary>
    protected ObjectResult SuccessCreated(object? value, string message) =>
        StatusCode(StatusCodes.Status201Created, ResponseBuilder.SuccessResponse(
            value, message, StatusCodes.Status201Created, HttpContext.TraceIdentifier));

    /// <summary>Maps validation failures to <c>{ field, message }</c> items; root-level failures use field <c>$</c>.</summary>
    protected static object[] FieldErrors(ValidationResult validation) => validation.Errors
        .Select(error => (object)new
        {
            field = string.IsNullOrEmpty(error.PropertyName)
                ? "$"
                : char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..],
            message = error.ErrorMessage
        })
        .ToArray();

    /// <summary>Validates a request; returns a 400 envelope listing every failure, or null when valid.</summary>
    protected async Task<BadRequestObjectResult?> ValidateRequestAsync<T>(
        T request,
        IValidator<T> validator,
        CancellationToken cancellationToken,
        string errorCode = "VALIDATION_ERROR",
        string message = "The request data is invalid",
        string? traceId = null)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (validation.IsValid) return null;
        return BadRequest(ResponseBuilder.ErrorResponse(FieldErrors(validation), message, errorCode, traceId: traceId));
    }
}
