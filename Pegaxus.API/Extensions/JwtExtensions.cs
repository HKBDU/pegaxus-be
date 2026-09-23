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

    public static void AddJwtServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOption = new JwtOptions();

        configuration
            .GetSection(nameof(JwtOptions))
            .Bind(jwtOption);

        var key = Encoding.UTF8.GetBytes(jwtOption.SecretKey);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    JwtBearerDefaults.AuthenticationScheme;

                options.DefaultChallengeScheme =
                    JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwtOption.Issuer,
                    ValidAudience = jwtOption.Audience,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(key),

                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        services.AddAuthorization(options =>
        {
            // Admin
            options.AddPolicy(AdminPolicy, policy =>
                policy.RequireRole("Admin"));

            // Customer
            options.AddPolicy(CustomerPolicy, policy =>
                policy.RequireRole("Customer"));

            // Employee
            options.AddPolicy(EmployeePolicy, policy =>
                policy.RequireRole("Employee"));

            // Logistics Manager
            options.AddPolicy(LogisticsManagerPolicy, policy =>
                policy.RequireRole("LogisticsManager"));

            // Transport Specialist
            options.AddPolicy(TransportSpecialistPolicy, policy =>
                policy.RequireRole("TransportSpecialist"));

            // Fleet & Route Coordinator
            options.AddPolicy(FleetRouteCoordinatorPolicy, policy =>
                policy.RequireRole("FleetRouteCoordinator"));

            // Driver
            options.AddPolicy(DriverPolicy, policy =>
                policy.RequireRole("Driver"));

            // Escort
            options.AddPolicy(EscortPolicy, policy =>
                policy.RequireRole("Escort"));

            // Customer OR Employee
            options.AddPolicy(CustomerOrEmployeePolicy, policy =>
                policy.RequireRole("Customer", "Employee"));
        });
    }
}
