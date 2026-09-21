using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.JwtService;

public class JwtOptions
{
    [Required] public string Issuer { get; set; } = null!;
    [Required] public string Audience { get; set; } = null!;
    [Required] public string SecretKey { get; set; } = null!;
    public int ExpirationMinutes { get; set; } 
}
