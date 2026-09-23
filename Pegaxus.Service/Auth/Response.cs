namespace Pegaxus.Service.Auth;

public class Response
{
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
