namespace Pegaxus.Service.Auth;


public class Response
{
    public class LoginResponse
    {
        public required string AccessToken { get; set; }
        public required string RefreshToken { get; set; }
        public string TokenType { get; set; } = "Bearer";
        public DateTime ExpiresAt { get; set; }
        public required UserInfoResponse Account { get; set; }
    }

    public class UserInfoResponse
    {
        public Guid AccountId { get; set; }
        public required string Email { get; set; }
        public required string FullName { get; set; }
        public required string AccountType { get; set; }
        public required string Role { get; set; }
    }

    public class RegisterResponse
    {
        public Guid AccountId { get; set; }
        public Guid CustomerId { get; set; }
        public required string Email { get; set; }
        public bool EmailVerified { get; set; }
    }
}
