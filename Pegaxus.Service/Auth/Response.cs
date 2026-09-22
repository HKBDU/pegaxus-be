namespace Pegaxus.Service.Auth;


public class Response
{
    public class RegisterResponse
    {
        public Guid AccountId { get; set; }
        public Guid CustomerId { get; set; }
        public required string Email { get; set; }
        public bool EmailVerified { get; set; }
    }
}