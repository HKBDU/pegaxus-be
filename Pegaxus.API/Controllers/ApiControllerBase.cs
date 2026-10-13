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
}
