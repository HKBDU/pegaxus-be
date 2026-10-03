using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.Repository.Enum;
using AppException = Pegaxus.Service.Exceptions.Exceptions;

namespace Pegaxus.Service.JwtService;

public class JwtServices : IJwtService
{

    public static string GetRoleName(AccountType accountType) => accountType switch
    {
        AccountType.CUSTOMER => "Customer",
        AccountType.EMPLOYEE => "Employee",
        _ => throw new AppException("Session has expired. Please sign in again", 401, "REFRESH_TOKEN_INVALID")
    };

    public static string GetRoleName(EmployeeRole role) => role switch
    {
        EmployeeRole.LOGISTICS_MANAGER => "LogisticsManager",
        EmployeeRole.TRANSPORT_SPECIALIST => "TransportSpecialist",
        EmployeeRole.FLEET_ROUTE_COORDINATOR => "FleetRouteCoordinator",
        EmployeeRole.DRIVER => "Driver",
        EmployeeRole.ESCORT => "Escort",
        _ => throw new AppException("Session has expired. Please sign in again", 401, "REFRESH_TOKEN_INVALID")
    };

    private readonly JwtOptions _jwtOption = new();

    public JwtServices(IConfiguration configuration)
    {
        configuration.GetSection(nameof(JwtOptions)).Bind(_jwtOption);
        // Ánh xạ dữ liệu từ AppSettings vào Object JwtOptions
    }

    public int AccessTokenExpirationSeconds => (int)TimeSpan.FromMinutes(_jwtOption.ExpirationMinutes).TotalSeconds;

    public string GenerateAccessToken(IEnumerable<Claim> claims)
    {
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOption.SecretKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOption.Issuer,
            audience: _jwtOption.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOption.ExpirationMinutes),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken() =>
        $"rt_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";

    public ClaimsPrincipal ValidateToken(string token)
    {
        return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwtOption.Issuer,
            ValidAudience = _jwtOption.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOption.SecretKey)),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.Zero
        }, out _);
    }
}
