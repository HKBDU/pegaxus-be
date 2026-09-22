namespace Pegaxus.Service.Exceptions;

public class Exceptions : Exception
{
    public int StatusCode { get; set; }
    
    public Exceptions(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

}
public class BadRequestException : Exceptions
{
    public BadRequestException(string message) : base(message, 400) { }
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
}

public class ConflictException : Exceptions
{
    public ConflictException(string message) : base(message, 409) { }
}

public class TooManyRequestsException : Exceptions
{
    public TooManyRequestsException(string message) : base(message, 429) { }
}
