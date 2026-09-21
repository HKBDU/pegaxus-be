using System.Security.Claims;

namespace Pegaxus.Service.JwtService;

public interface IJwtService
{
    public string GenerateAccessToken(IEnumerable<Claim> claims);
    public string GenerateRefreshToken();
    ClaimsPrincipal ValidateToken(string token);
}