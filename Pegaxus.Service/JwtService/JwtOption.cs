using System.ComponentModel.DataAnnotations;

namespace Pegaxus.Service.JwtService;

public class JwtOptions
{
    [Required]public string Issuer { get; set; }
    [Required]public string Audience { get; set; }
    [Required]public string SecretKey { get; set; }
    [Required]public double ExpireMinutes { get; set; }
}