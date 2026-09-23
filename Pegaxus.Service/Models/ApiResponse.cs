using System.Text.Json.Serialization;

namespace Pegaxus.Service.Models;

public class ApiResponse
{
    public bool IsSuccess { get; set; }
    public int Code { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }
    public required string Message { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Errors { get; set; }
    public object? Value { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public static class ResponseBuilder
{
    public static ApiResponse SuccessResponse(object? value, string message, int code = 200, string? traceId = null)
    {
        return new ApiResponse()
        {
            IsSuccess = true,
            Code = code,
            Message = message,
            Value = value,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public static ApiResponse ErrorResponse(
        object? errors,
        string message,
        string errorCode = "VALIDATION_ERROR",
        int code = 400,
        string? traceId = null)
    {
        return new ApiResponse()
        {
            IsSuccess = false,
            Code = code,
            ErrorCode = errorCode,
            Message = message,
            Errors = errors,
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
