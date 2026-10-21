using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pegaxus.API.Models;
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;

namespace Pegaxus.API.Extensions;

public static class JwtExtensions
{
    public const string CustomerPolicy = "CustomerPolicy";
    public const string EmployeePolicy = "EmployeePolicy";
    public const string LogisticsManagerPolicy = "LogisticsManagerPolicy";
    public const string LogisticsManagerOrFleetRouteCoordinatorPolicy = "LogisticsManagerOrFleetRouteCoordinatorPolicy";
    public const string TransportSpecialistPolicy = "TransportSpecialistPolicy";
    public const string FleetRouteCoordinatorPolicy = "FleetRouteCoordinatorPolicy";
    public const string VehicleManagerPolicy = "VehicleManagerPolicy";
    public const string DriverPolicy = "DriverPolicy";
    public const string EscortPolicy = "EscortPolicy";
    public const string CustomerOrEmployeePolicy = "CustomerOrEmployeePolicy";
    public const string CustomerOrLogisticsManagerPolicy = "CustomerOrLogisticsManagerPolicy";
    public const string CustomerOrTransportSpecialistPolicy = "CustomerOrTransportSpecialistPolicy";
    public const string TransportSpecialistOrFleetRouteCoordinatorPolicy = "TransportSpecialistOrFleetRouteCoordinatorPolicy";
    public const string LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy = "LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy";
    public const string TransportSpecialistOrLogisticsManagerPolicy = "TransportSpecialistOrLogisticsManagerPolicy";
    public const string PersonnelAssignmentViewerPolicy = nameof(PersonnelAssignmentViewerPolicy);
    public const string BookingAssignmentViewerPolicy = "BookingAssignmentViewerPolicy";
    public const string DriverOrEscortPolicy = "DriverOrEscortPolicy";
    public const string ComplianceDossierViewerPolicy = "ComplianceDossierViewerPolicy";
    public const string TransportProviderViewerPolicy = "TransportProviderViewerPolicy";
    public const string HandoverViewerPolicy = nameof(HandoverViewerPolicy);
    public const string HandoverOperatorPolicy = nameof(HandoverOperatorPolicy);
    public const string TransportTrackingViewerPolicy = nameof(TransportTrackingViewerPolicy);
    public const string TransportProgressViewerPolicy = nameof(TransportProgressViewerPolicy);
    public const string VehicleIncidentOperatorPolicy = nameof(VehicleIncidentOperatorPolicy);

    public static void AddJwtServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOption = new JwtOptions();

        configuration
            .GetSection(nameof(JwtOptions))
            .Bind(jwtOption);

        if (string.IsNullOrWhiteSpace(jwtOption.SecretKey) || jwtOption.SecretKey.Length < 32)
            throw new InvalidOperationException("JwtOptions:SecretKey must contain at least 32 characters");
        if (jwtOption.ExpirationMinutes <= 0)
            throw new InvalidOperationException("JwtOptions:ExpirationMinutes must be greater than 0");

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
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = jwtOption.Issuer,
                    ValidAudience = jwtOption.Audience,

                    IssuerSigningKey =
                        new SymmetricSecurityKey(key),

                    NameClaimType = ClaimTypes.NameIdentifier,
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrWhiteSpace(accessToken) &&
                            (path.StartsWithSegments("/hubs/transport-tracking") || path.StartsWithSegments("/hubs/notifications") ||
                             path.StartsWithSegments("/hubs/compliance-dossiers")))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(ResponseBuilder.ErrorResponse(
                            null,
                            "The authentication session is invalid or has expired",
                            "UNAUTHORIZED",
                            StatusCodes.Status401Unauthorized));
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        var invoiceListDenied = HttpMethods.IsGet(context.Request.Method) &&
                            string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/v1/invoices", StringComparison.OrdinalIgnoreCase) &&
                            context.HttpContext.User.IsInRole(JwtServices.GetRoleName(AccountType.CUSTOMER));
                        await context.Response.WriteAsJsonAsync(ResponseBuilder.ErrorResponse(
                            null,
                            invoiceListDenied ? "Customer cannot access the invoice list" : "You do not have permission to perform this action",
                            "FORBIDDEN",
                            StatusCodes.Status403Forbidden));
                    },
                    OnTokenValidated = async context =>
                    {
                        var accountIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.Principal?.FindFirstValue("sub")
                            ?? context.Principal?.FindFirstValue("accountId");
                        if (!Guid.TryParse(accountIdValue, out var accountId))
                        {
                            context.Fail("The authenticated account identifier is invalid");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var account = await db.Accounts
                            .AsNoTracking()
                            .Include(item => item.CustomerAccount)
                            .Include(item => item.EmployeeAccount)
                            .SingleOrDefaultAsync(item => item.AccountId == accountId);

                        var valid = account is not null &&
                            !account.IsDeleted &&
                            account.Status == AccountStatus.ACTIVE &&
                            (account.AccountType != AccountType.CUSTOMER || account.CustomerAccount is { IsDeleted: false }) &&
                            (account.AccountType != AccountType.EMPLOYEE || account.EmployeeAccount is { IsDeleted: false, IsActive: true });
                        if (!valid)
                            context.Fail("The account is inactive or unavailable");
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(VehicleIncidentOperatorPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(
                JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                JwtServices.GetRoleName(EmployeeRole.DRIVER), JwtServices.GetRoleName(EmployeeRole.ESCORT)));
            options.AddPolicy(HandoverViewerPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(
                JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(EmployeeRole.DRIVER),
                JwtServices.GetRoleName(EmployeeRole.ESCORT), JwtServices.GetRoleName(AccountType.CUSTOMER)));
            // Specialists pass this policy only to schedule a handover; the service refuses them on start and complete.
            options.AddPolicy(HandoverOperatorPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(
                JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST),
                JwtServices.GetRoleName(EmployeeRole.DRIVER), JwtServices.GetRoleName(EmployeeRole.ESCORT)));
            options.AddPolicy(TransportTrackingViewerPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(
                JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(AccountType.CUSTOMER)));
            options.AddPolicy(TransportProgressViewerPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(
                JwtServices.GetRoleName(AccountType.CUSTOMER), JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                JwtServices.GetRoleName(EmployeeRole.DRIVER), JwtServices.GetRoleName(EmployeeRole.ESCORT)));

            options.AddPolicy(CustomerOrLogisticsManagerPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(JwtServices.GetRoleName(AccountType.CUSTOMER),
                    JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER)));
            options.AddPolicy(LogisticsManagerOrFleetRouteCoordinatorPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));
            options.AddPolicy(CustomerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(AccountType.CUSTOMER)));

            options.AddPolicy(EmployeePolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(AccountType.EMPLOYEE)));

            options.AddPolicy(LogisticsManagerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER)));

            options.AddPolicy(TransportSpecialistPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST)));

            options.AddPolicy(FleetRouteCoordinatorPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));

            options.AddPolicy(VehicleManagerPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                    JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER)));

            options.AddPolicy(DriverPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.DRIVER)));

            options.AddPolicy(EscortPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.ESCORT)));

            options.AddPolicy(CustomerOrEmployeePolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(AccountType.CUSTOMER), JwtServices.GetRoleName(AccountType.EMPLOYEE)));

            options.AddPolicy(TransportSpecialistOrLogisticsManagerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER)));

            options.AddPolicy(DriverOrEscortPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.DRIVER), JwtServices.GetRoleName(EmployeeRole.ESCORT)));

            options.AddPolicy(ComplianceDossierViewerPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(JwtServices.GetRoleName(AccountType.CUSTOMER),
                    JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST), JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER)));
            options.AddPolicy(CustomerOrTransportSpecialistPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireRole(JwtServices.GetRoleName(AccountType.CUSTOMER),
                    JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST)));
            options.AddPolicy(TransportSpecialistOrFleetRouteCoordinatorPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));
            options.AddPolicy(LogisticsManagerOrTransportSpecialistOrFleetRouteCoordinatorPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                    JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));
            options.AddPolicy(PersonnelAssignmentViewerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                    JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR),
                    JwtServices.GetRoleName(EmployeeRole.DRIVER), JwtServices.GetRoleName(EmployeeRole.ESCORT)));
            options.AddPolicy(BookingAssignmentViewerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                    JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));
            options.AddPolicy(TransportProviderViewerPolicy, policy =>
                policy.RequireRole(JwtServices.GetRoleName(EmployeeRole.LOGISTICS_MANAGER),
                    JwtServices.GetRoleName(EmployeeRole.FLEET_ROUTE_COORDINATOR)));
        });
    }
}
