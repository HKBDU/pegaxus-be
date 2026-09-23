namespace Pegaxus.Service.Auth;


public class Response
{
    public class LoginResponse
    {
        public required string AccessToken { get; set; }
        public required string RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public required UserInfoResponse User { get; set; }
    }

    public class UserInfoResponse
    {
        public Guid AccountId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? EmployeeId { get; set; }
        public required string FullName { get; set; }
        public required string AccountType { get; set; }
        public required string Role { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class RegisterResponse
    {
        public Guid AccountId { get; set; }
        public Guid CustomerId { get; set; }
    }

    public class ResendOtpResponse
    {
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public class RefreshTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
    }
}