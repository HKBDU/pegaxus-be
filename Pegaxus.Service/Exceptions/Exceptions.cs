namespace Pegaxus.Service.Exceptions;

public class Exceptions : Exception
{
    public int StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    
    public Exceptions(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public Exceptions(string message, int statusCode, string errorCode) : this(message, statusCode)
    {
        ErrorCode = errorCode;
    }

}
public class BadRequestException : Exceptions
{
    public BadRequestException(string message) : base(message, 400) { }
    public BadRequestException(string message, string errorCode) : base(message, 400, errorCode) { }
}

public class UnauthorizedException : Exceptions
{
    public UnauthorizedException(string message) : base(message, 401) { }
}

public class ForbiddenException : Exceptions
{
    public ForbiddenException(string message) : base(message, 403) { }
}

public class NotFoundException : Exceptions
{
    public NotFoundException(string message) : base(message, 404) { }
    public NotFoundException(string message, string errorCode) : base(message, 404, errorCode) { }
}

public class ConflictException : Exceptions
{
    public ConflictException(string message) : base(message, 409) { }
    public ConflictException(string message, string errorCode) : base(message, 409, errorCode) { }
}

public class TooManyRequestsException : Exceptions
{
    public TooManyRequestsException(string message) : base(message, 429) { }
}
