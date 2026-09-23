namespace Pegaxus.Service.Exceptions;

public class Exceptions : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    
    public Exceptions(string message, int statusCode = 400, string errorCode = "VALIDATION_ERROR") : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

}
public class BadRequestException : Exceptions
{
    public BadRequestException(string message, string errorCode = "VALIDATION_ERROR") : base(message, 400, errorCode) { }
}

public class UnauthorizedException : Exceptions
{
    public UnauthorizedException(string message, string errorCode = "UNAUTHORIZED") : base(message, 401, errorCode) { }
}

public class ForbiddenException : Exceptions
{
    public ForbiddenException(string message, string errorCode = "FORBIDDEN") : base(message, 403, errorCode) { }
}

public class NotFoundException : Exceptions
{
    public NotFoundException(string message, string errorCode = "NOT_FOUND") : base(message, 404, errorCode) { }
}

public class ConflictException : Exceptions
{
    public ConflictException(string message, string errorCode = "CONFLICT") : base(message, 409, errorCode) { }
}

public class TooManyRequestsException : Exceptions
{
    public TooManyRequestsException(string message, string errorCode = "TOO_MANY_REQUESTS") : base(message, 429, errorCode) { }
}

public class UnprocessableEntityException : Exceptions
{
    public UnprocessableEntityException(string message, string errorCode) : base(message, 422, errorCode) { }
}
