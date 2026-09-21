using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.Service.JwtService;

namespace Pegaxus.API.Extensions;

public static class JwtExtensions
{
    public const string AdminPolicy = "AdminPolicy";
    public const string CustomerPolicy = "CustomerPolicy";
    public const string EmployeePolicy = "EmployeePolicy";
    public const string LogisticsManagerPolicy = "LogisticsManagerPolicy";
    public const string TransportSpecialistPolicy = "TransportSpecialistPolicy";
    public const string FleetRouteCoordinatorPolicy = "FleetRouteCoordinatorPolicy";
    public const string DriverPolicy = "DriverPolicy";
    public const string EscortPolicy = "EscortPolicy";
    public const string CustomerOrEmployeePolicy = "CustomerOrEmployeePolicy";

    public static void AddJwtServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(nameof(JwtOptions)).Get<JwtOptions>() ?? new JwtOptions();
        jwtOptions.SecretKey = configuration["JWT_SECRET"] ?? configuration["JwtSettings:Secret"] ?? jwtOptions.SecretKey;
        jwtOptions.Issuer = configuration["JWT_ISSUER"] ?? configuration["JwtSettings:Issuer"] ?? jwtOptions.Issuer;
        jwtOptions.Audience = configuration["JWT_AUDIENCE"] ?? configuration["JwtSettings:Audience"] ?? jwtOptions.Audience;

        var expiration = configuration["JWT_EXPIRY_MINUTES"] ?? configuration["JwtSettings:ExpiryInMinutes"];
        if (int.TryParse(expiration, out var expirationMinutes))
            jwtOptions.ExpirationMinutes = expirationMinutes;

        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer)
            || string.IsNullOrWhiteSpace(jwtOptions.Audience)
            || Encoding.UTF8.GetByteCount(jwtOptions.SecretKey) < 32
            || jwtOptions.ExpirationMinutes <= 0)
            throw new InvalidOperationException("JWT configuration is missing or invalid.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey));
        services.AddSingleton(jwtOptions);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = signingKey,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role,
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminPolicy, policy => policy.RequireRole("Admin"));
            options.AddPolicy(CustomerPolicy, policy => policy.RequireRole("Customer"));
            options.AddPolicy(EmployeePolicy, policy => policy.RequireRole("Employee"));
            options.AddPolicy(LogisticsManagerPolicy, policy => policy.RequireRole("LogisticsManager"));
            options.AddPolicy(TransportSpecialistPolicy, policy => policy.RequireRole("TransportSpecialist"));
            options.AddPolicy(FleetRouteCoordinatorPolicy, policy => policy.RequireRole("FleetRouteCoordinator"));
            options.AddPolicy(DriverPolicy, policy => policy.RequireRole("Driver"));
            options.AddPolicy(EscortPolicy, policy => policy.RequireRole("Escort"));
            options.AddPolicy(CustomerOrEmployeePolicy, policy => policy.RequireRole("Customer", "Employee"));
        });
    }
}
