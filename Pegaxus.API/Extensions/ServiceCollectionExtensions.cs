using FluentValidation;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Hubs;
using Pegaxus.API.Middleware;
using Pegaxus.API.Models;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;

using AuthService = Pegaxus.Service.Auth;
using BookingService = Pegaxus.Service.BookingService;
using TransportPlanService = Pegaxus.Service.TransportPlanService;
using RoutePlanService = Pegaxus.Service.RoutePlanService;
using LocationService = Pegaxus.Service.LocationService;
using RouteService = Pegaxus.Service.RouteService;
using JwtService = Pegaxus.Service.JwtService;
using MediaService = Pegaxus.Service.MediaService;
using CloudinaryService = Pegaxus.Service.CloudinaryService;
using MailService = Pegaxus.Service.MailService;
using AccountService = Pegaxus.Service.AccountService;
using HorseService = Pegaxus.Service.HorseService;
using EmployeeService = Pegaxus.Service.EmployeeService;
using ComplianceDossierService = Pegaxus.Service.ComplianceDossierService;
using NotificationService = Pegaxus.Service.NotificationService;
using DossierTemplateService = Pegaxus.Service.DossierTemplateService;
using ComplianceRequirementService = Pegaxus.Service.ComplianceRequirementService;

namespace Pegaxus.API.Extensions;

/// <summary>Composition-root registrations, grouped by feature module.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services
            .AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .SelectMany(entry => entry.Value!.Errors.Select(error => new
                        {
                            field = string.IsNullOrEmpty(entry.Key)
                                ? "$"
                                : char.ToLowerInvariant(entry.Key[0]) + entry.Key[1..],
                            message = error.ErrorMessage
                        }))
                        .ToArray();

                    return new BadRequestObjectResult(ResponseBuilder.ErrorResponse(
                        errors,
                        "The request data is invalid",
                        traceId: context.HttpContext.TraceIdentifier));
                };
            });
        return services;
    }

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = (configuration["ALLOWED_ORIGINS"] ??
                       "http://localhost:5173;https://pegaxus-fe.vercel.app;https://pegaxus-fe-git-dev-pegaxus.vercel.app")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
        return services;
    }

    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, AccountUserIdProvider>();
        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(
            configuration.GetConnectionString("DefaultConnection"));
        var enumNameTranslator = new PostgresEnumNameTranslator();
        foreach (var enumType in typeof(AccountStatus).Assembly.GetTypes()
                     .Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
        {
            dataSourceBuilder.MapEnum(
                enumType,
                enumNameTranslator.TranslateTypeName(enumType.Name),
                enumNameTranslator);
        }

        services.AddSingleton(dataSourceBuilder.Build());
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        const string googleExternalScheme = "GoogleExternal";
        var googleClientId = configuration["Google:ClientId"];
        var googleClientSecret = configuration["Google:ClientSecret"];
        var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId) &&
                               !string.IsNullOrWhiteSpace(googleClientSecret);
        if (!googleConfigured)
            return services;

        services.AddAuthentication()
            .AddCookie(googleExternalScheme, options =>
            {
                options.Cookie.Name = "pegaxus.google.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            })
            .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = googleClientId!;
                options.ClientSecret = googleClientSecret!;
                options.SignInScheme = googleExternalScheme;
                options.CallbackPath = "/api/v1/auth/google/callback";
                options.CorrelationCookie.Name = "pegaxus.google.correlation";
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.IsEssential = true;
                options.CorrelationCookie.Path = "/";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.SaveTokens = false;
                options.Events.OnRemoteFailure = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("GoogleAuthentication");
                    logger.LogError(
                        context.Failure,
                        "Google OAuth failed for {Path}. Error={Error}, ErrorDescription={ErrorDescription}",
                        context.HttpContext.Request.Path,
                        context.Request.Query["error"].ToString(),
                        context.Request.Query["error_description"].ToString());
                    logger.LogError(
                        "Google OAuth callback cookies: {CookieNames}",
                        string.Join(", ", context.HttpContext.Request.Cookies.Keys));

                    var frontendUrl = configuration["Cors:FrontendUrl"] ?? "http://localhost:5173";
                    context.Response.Redirect(
                        $"{frontendUrl.TrimEnd('/')}/auth/google/callback?error=GOOGLE_AUTHENTICATION_FAILED");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        return services;
    }

    public static IServiceCollection AddRedisCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379");
        return services;
    }

    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddTransient<GlobalExceptionHandlerMiddleware>();
        return services;
    }

    /// <summary>Authentication, JWT and password hashing.</summary>
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddScoped<JwtService.IJwtService, JwtService.JwtServices>();
        services.AddScoped<AuthService.IAuthService, AuthService.Service>();
        services.AddScoped<IValidator<AuthService.Request.LoginRequest>, AuthService.Validations.LoginRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.RegisterRequest>, AuthService.Validations.RegisterRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.ForgotPasswordRequest>, AuthService.Validations.ForgotPasswordRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.VerifyEmailRequest>, AuthService.Validations.VerifyEmailRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.ResendOtpRequest>, AuthService.Validations.ResendOtpRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.RefreshTokenRequest>, AuthService.Validations.RefreshTokenRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.LogoutRequest>, AuthService.Validations.LogoutRequestValidator>();
        services.AddScoped<IValidator<AuthService.Request.ResetPasswordRequest>, AuthService.Validations.ResetPasswordRequestValidator>();
        services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
        return services;
    }

    /// <summary>Account profiles, horses and employees.</summary>
    public static IServiceCollection AddAccountsModule(this IServiceCollection services)
    {
        services.AddScoped<AccountService.IAccountService, AccountService.Service>();
        services.AddScoped<HorseService.IHorseService, HorseService.Service>();
        services.AddScoped<EmployeeService.IEmployeeService, EmployeeService.Service>();
        return services;
    }

    /// <summary>Customer bookings and their lifecycle.</summary>
    public static IServiceCollection AddBookingModule(this IServiceCollection services)
    {
        services.AddScoped<BookingService.IBookingService, BookingService.Service>();
        services.AddScoped<IValidator<BookingService.Request.CreateBookingRequest>, BookingService.Validations.CreateBookingRequestValidator>();
        services.AddScoped<IValidator<BookingService.Request.UpdateBookingRequest>, BookingService.Validations.UpdateBookingRequestValidator>();
        services.AddScoped<IValidator<BookingService.Request.CancelBookingRequest>, BookingService.Validations.CancelBookingRequestValidator>();
        services.AddScoped<IValidator<BookingService.Request.RejectBookingRequest>, BookingService.Validations.RejectBookingRequestValidator>();
        services.AddScoped<IValidator<BookingService.Request.NegotiateBookingRequest>, BookingService.Validations.NegotiateBookingRequestValidator>();
        return services;
    }

    /// <summary>Invoices, online payment checkout and the payment webhook.</summary>
    public static IServiceCollection AddInvoicingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Pegaxus.Service.InvoiceService.IInvoiceService, Pegaxus.Service.InvoiceService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.BookingListRequest>, Pegaxus.Service.InvoiceService.Validator.BookingListRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.ListRequest>, Pegaxus.Service.InvoiceService.Validator.ListRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.UpdateRequest>, Pegaxus.Service.InvoiceService.Validator.UpdateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.CreateRequest>, Pegaxus.Service.InvoiceService.Validator.CreateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.SuggestionRequest>, Pegaxus.Service.InvoiceService.Validator.SuggestionRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.IssueRequest>, Pegaxus.Service.InvoiceService.Validator.IssueRequestValidator>();
        services.Configure<Pegaxus.Service.PaymentService.SePayWebhookOptions>(configuration.GetSection("SePayWebhookOptions"));
        services.AddScoped<Pegaxus.Service.PaymentService.IPaymentService, Pegaxus.Service.PaymentService.Service>();
        services.Configure<Pegaxus.Service.PaymentGatewayService.SePayOptions>(configuration.GetSection(nameof(Pegaxus.Service.PaymentGatewayService.SePayOptions)));
        services.AddScoped<Pegaxus.Service.PaymentGatewayService.IPaymentGatewayService, Pegaxus.Service.PaymentGatewayService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.RecordPaymentRequest>, Pegaxus.Service.InvoiceService.Validator.RecordPaymentRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.InvoiceService.Request.CancelRequest>, Pegaxus.Service.InvoiceService.Validator.CancelRequestValidator>();
        return services;
    }

    /// <summary>Transport plans, route plans, locations, routes, emergency route changes, outsourcing and personnel assignment.</summary>
    public static IServiceCollection AddTransportPlanningModule(this IServiceCollection services)
    {
        services.AddScoped<Pegaxus.Service.TransportService.ITransportService, Pegaxus.Service.TransportService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportService.Request.AssignedRequest>, Pegaxus.Service.TransportService.Validator.AssignedRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportService.Request.ListRequest>, Pegaxus.Service.TransportService.Validator.ListRequestValidator>();
        services.AddScoped<TransportPlanService.ITransportPlanService, TransportPlanService.Service>();
        services.AddScoped<RoutePlanService.IRoutePlanService, RoutePlanService.Service>();
        services.AddScoped<Pegaxus.Service.NotificationService.TransportNotifications>();
        services.AddScoped<LocationService.ILocationService, LocationService.Service>();
        services.AddScoped<RouteService.IRouteService, RouteService.Service>();
        services.AddScoped<Pegaxus.Service.RouteChangeService.IRouteChangeService, Pegaxus.Service.RouteChangeService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.CreateRequest>, Pegaxus.Service.RouteChangeService.Validator.CreateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.DecisionRequest>, Pegaxus.Service.RouteChangeService.Validator.DecisionRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.ActivationRequest>, Pegaxus.Service.RouteChangeService.Validator.ActivationRequestValidator>();
        services.AddScoped<Pegaxus.Service.PersonnelAssignmentService.IPersonnelAssignmentService, Pegaxus.Service.PersonnelAssignmentService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.PersonnelAssignmentService.Request.AssignRequest>, Pegaxus.Service.PersonnelAssignmentService.Validator.AssignRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.PersonnelAssignmentService.Request.ReplaceRequest>, Pegaxus.Service.PersonnelAssignmentService.Validator.ReplaceRequestValidator>();
        services.AddScoped<Pegaxus.Service.RouteOutsourcingService.IRouteOutsourcingService, Pegaxus.Service.RouteOutsourcingService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.RouteOutsourcingService.Request>, Pegaxus.Service.RouteOutsourcingService.Validator.RequestValidator>();
        return services;
    }

    /// <summary>Vehicle and transport provider catalogs.</summary>
    public static IServiceCollection AddFleetModule(this IServiceCollection services)
    {
        services.AddScoped<Pegaxus.Service.VehicleService.IVehicleService, Pegaxus.Service.VehicleService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.ListRequest>, Pegaxus.Service.VehicleService.Validator.ListRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.CreateRequest>, Pegaxus.Service.VehicleService.Validator.CreateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.UpdateRequest>, Pegaxus.Service.VehicleService.Validator.UpdateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.StatusRequest>, Pegaxus.Service.VehicleService.Validator.StatusRequestValidator>();
        services.AddScoped<Pegaxus.Service.TransportProviderService.ITransportProviderService, Pegaxus.Service.TransportProviderService.Service>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.ListRequest>, Pegaxus.Service.TransportProviderService.Validator.ListRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.CreateRequest>, Pegaxus.Service.TransportProviderService.Validator.CreateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.UpdateRequest>, Pegaxus.Service.TransportProviderService.Validator.UpdateRequestValidator>();
        services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.StatusRequest>, Pegaxus.Service.TransportProviderService.Validator.StatusRequestValidator>();
        return services;
    }

    /// <summary>Compliance dossiers, templates, requirement catalog, document storage and reminders.</summary>
    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<Pegaxus.Service.DocumentStorageService.IDocumentStorageService, Pegaxus.Service.DocumentStorageService.Service>();
        services.AddScoped<ComplianceDossierService.IComplianceDossierService, ComplianceDossierService.Service>();
        services.AddHostedService<Pegaxus.API.Services.MissingDocumentReminderWorker>();
        services.AddScoped<IValidator<ComplianceDossierService.Request.InitializeRequest>, ComplianceDossierService.Validator.InitializeRequestValidator>();
        services.AddScoped<DossierTemplateService.IDossierTemplateService, DossierTemplateService.Service>();
        services.AddScoped<IValidator<DossierTemplateService.Request.ListRequest>, DossierTemplateService.Validator.ListRequestValidator>();
        services.AddScoped<IValidator<DossierTemplateService.Request.SaveRequest>, DossierTemplateService.Validator.SaveRequestValidator>();
        services.AddScoped<IValidator<DossierTemplateService.Request.ChangeStatusRequest>, DossierTemplateService.Validator.ChangeStatusRequestValidator>();
        services.AddScoped<ComplianceRequirementService.IComplianceRequirementService, ComplianceRequirementService.Service>();
        services.AddScoped<IValidator<ComplianceRequirementService.Request.ListRequest>, ComplianceRequirementService.Validator.ListRequestValidator>();
        services.AddScoped<IValidator<ComplianceRequirementService.Request.SaveRequest>, ComplianceRequirementService.Validator.SaveRequestValidator>();
        services.AddScoped<IValidator<ComplianceRequirementService.Request.StatusRequest>, ComplianceRequirementService.Validator.StatusRequestValidator>();
        return services;
    }

    /// <summary>Mail, media storage, notifications and the transactional outbox.</summary>
    public static IServiceCollection AddMessagingModule(this IServiceCollection services)
    {
        services.AddScoped<MediaService.IMediaService, CloudinaryService.Service>();
        services.AddScoped<MailService.IMailService, MailService.Service>();
        services.AddHostedService<Pegaxus.API.Services.TransactionalOutboxWorker>();
        services.AddScoped<NotificationService.INotificationService, NotificationService.Service>();
        services.AddScoped<NotificationService.INotificationRealtimeService, SignalRNotificationRealtimeService>();
        return services;
    }

    private sealed class PostgresEnumNameTranslator : INpgsqlNameTranslator
    {
        private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();

        public string TranslateTypeName(string clrName) => _snakeCase.TranslateTypeName(clrName);

        public string TranslateMemberName(string clrName) => clrName;
    }
}
