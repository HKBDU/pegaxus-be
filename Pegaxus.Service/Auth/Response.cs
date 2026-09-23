namespace Pegaxus.Service.Auth;

public class Response
{
    public class RegisterResponse
    {
        public Guid AccountId { get; set; }
        public Guid CustomerId { get; set; }
    }
}