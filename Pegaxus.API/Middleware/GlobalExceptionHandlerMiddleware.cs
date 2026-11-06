using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pegaxus.API.Models;
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
            // Logging must not replace the original API error response. Some hosts
            // (notably Windows EventLog without source permissions) can throw while
            // writing a log entry, which otherwise closes the HTTP response early.
            try
            {
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            catch
            {
                // Preserve the mapped error response when a logging provider fails.
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(ResponseBuilder.ErrorResponse(
                FieldErrors((ex as FieldValidationException)?.Errors),
                // Only text written by our own code may reach the user; framework and database exceptions use the safe fallback.
                (ex as Exceptions)?.Message ?? string.Empty,
                errorCode: GetErrorCode(ex, statusCode),
                code: statusCode,
                traceId: context.TraceIdentifier,
                value: ex switch { ConflictException conflict => conflict.Value, UnprocessableEntityException rule => rule.Value, _ => null }));
        }
    }

    private static object? FieldErrors(IReadOnlyList<FieldError>? errors) => errors?.Select(error => new FieldError(
        error.Field,
        UserFacingMessages.FieldError(error.Field, error.Message))).ToArray();

    private static int MapStatusCode(Exception ex)
    {
        return ex switch
        {
            Exceptions appEx => appEx.StatusCode,
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => StatusCodes.Status413PayloadTooLarge,
            _ when ColumnOverflow(ex) == PostgresErrorCodes.NumericValueOutOfRange => StatusCodes.Status422UnprocessableEntity,
            _ when ColumnOverflow(ex) == PostgresErrorCodes.StringDataRightTruncation => StatusCodes.Status400BadRequest,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } } => StatusCodes.Status409Conflict,
            InvalidOperationException
            {
                InnerException:
                PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } or
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } }
            } => StatusCodes.Status409Conflict,
            ArgumentException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    /// <summary>
    /// Safety net for values that fit the validators but not the column. Validators must still reject them first;
    /// this only turns the leftover 500 into a 4xx without leaking database details.
    /// </summary>
    private static string? ColumnOverflow(Exception ex)
    {
        for (var inner = ex; inner is not null; inner = inner.InnerException)
            if (inner is PostgresException { SqlState: PostgresErrorCodes.NumericValueOutOfRange or PostgresErrorCodes.StringDataRightTruncation } pg)
                return pg.SqlState;
        return null;
    }

    private static string GetErrorCode(Exception ex, int statusCode)
    {
        if (ex is Exceptions appException)
            return appException.ErrorCode;
        switch (ColumnOverflow(ex))
        {
            case PostgresErrorCodes.NumericValueOutOfRange: return "VALUE_OUT_OF_RANGE";
            case PostgresErrorCodes.StringDataRightTruncation: return "VALUE_TOO_LONG";
        }

        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "VALIDATION_ERROR",
            StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
            StatusCodes.Status413PayloadTooLarge => "PAYLOAD_TOO_LARGE",
            StatusCodes.Status404NotFound => "NOT_FOUND",
            StatusCodes.Status409Conflict => "CONCURRENT_UPDATE",
            _ => "SYSTEM_ERROR"
        };
    }
}
