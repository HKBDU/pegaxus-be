using Pegaxus.Service.Models;
using Pegaxus.Service.Exceptions;

namespace Pegaxus.API.Middleware;

public class GlobalExceptionHandlerMiddleware : IMiddleware
{
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
                throw;

            var statusCode = MapStatusCode(ex);
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(ResponseBuilder.ErrorResponse(
                null,
                GetErrorMessage(ex, statusCode),
                errorCode: GetErrorCode(ex, statusCode),
                code: statusCode,
                traceId: context.TraceIdentifier));
        }
    }

    private static int MapStatusCode(Exception ex)
    {
        return ex switch
        {
            Exceptions appEx => appEx.StatusCode,
            ArgumentException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static string GetErrorMessage(Exception ex, int statusCode)
    {
        return statusCode >= StatusCodes.Status500InternalServerError
            ? "A system error occurred. Please try again later"
            : ex.Message;
    }

    private static string GetErrorCode(Exception ex, int statusCode)
    {
        if (ex is Exceptions appException)
            return appException.ErrorCode;

        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "VALIDATION_ERROR",
            StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
            StatusCodes.Status404NotFound => "NOT_FOUND",
            _ => "SYSTEM_ERROR"
        };
    }
}
