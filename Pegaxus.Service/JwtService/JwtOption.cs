using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.JwtService;

public class JwtOptions
{
    [Required] public string Issuer { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;
    [Required] public string SecretKey { get; set; } = string.Empty;
    [Range(1, double.MaxValue)] public double ExpirationMinutes { get; set; }
}
