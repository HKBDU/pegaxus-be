namespace Pegaxus.Service.Models;

public class ApiResponse
{
    public bool IsSuccess { get; set; }
    public required string Message { get; set; }
    public object? Data { get; set; }
    public object? Errors { get; set; }
    public string? ErrorCode { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public static class ResponseBuilder
{
    public static ApiResponse SuccessResponse(object? data, string message, string? traceId = null)
    {
        return new ApiResponse()
        {
            IsSuccess = true,
            Message = message,
            Data = data,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public static ApiResponse ErrorResponse(object? errors, string message, string? traceId = null, string? errorCode = null)
    {
        return new ApiResponse()
        {
            IsSuccess = false,
            Message = message,
            Errors = errors,
            ErrorCode = errorCode,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
