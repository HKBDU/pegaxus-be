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
                statusCode >= StatusCodes.Status500InternalServerError ? null : ex.Message,
                GetErrorMessage(ex, statusCode),
                context.TraceIdentifier));
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
            ? "Unexpected error occurred"
            : ex.Message;
    }
}
