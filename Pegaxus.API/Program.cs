using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Extensions;
using Pegaxus.API.Hubs;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Models;

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

var envDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (envDirectory is not null)
{
    var envPath = Path.Combine(envDirectory.FullName, ".env");
    if (File.Exists(envPath))
    {
        Env.Load(envPath);
        break;
    }

    envDirectory = envDirectory.Parent;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")))
    .SetApplicationName("Pegaxus");

// Keep issued access tokens at the 15-minute API contract lifetime in every environment.
builder.Configuration["JwtOptions:ExpirationMinutes"] = "15";

builder.Services
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(CloudinaryService.Service.PreviewHttpClient)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, AccountUserIdProvider>();

var dataSourceBuilder = new NpgsqlDataSourceBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection"));
var enumNameTranslator = new PostgresEnumNameTranslator();
foreach (var enumType in typeof(AccountStatus).Assembly.GetTypes()
             .Where(type => type.IsEnum && type.Namespace == typeof(AccountStatus).Namespace))
{
    dataSourceBuilder.MapEnum(
        enumType,
        enumNameTranslator.TranslateTypeName(enumType.Name),
        enumNameTranslator);
}

builder.Services.AddSingleton(dataSourceBuilder.Build());
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlDataSource>()));

// builder.Services.AddApplicationServices();
builder.Services.AddJwtServices(builder.Configuration);
const string googleExternalScheme = "GoogleExternal";
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];
var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId) &&
                       !string.IsNullOrWhiteSpace(googleClientSecret);
if (googleConfigured)
{
    builder.Services.AddAuthentication()
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

                var frontendUrl = builder.Configuration["Cors:FrontendUrl"] ?? "http://localhost:5173";
                context.Response.Redirect(
                    $"{frontendUrl.TrimEnd('/')}/auth/google/callback?error=GOOGLE_AUTHENTICATION_FAILED");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
}
builder.Services.AddSwaggerServices();
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");

builder.Services.AddScoped<JwtService.IJwtService, JwtService.JwtServices>();
builder.Services.AddScoped<AuthService.IService, AuthService.Service>();
builder.Services.AddScoped<IValidator<AuthService.Request.LoginRequest>, AuthService.Validations.LoginRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.RegisterRequest>, AuthService.Validations.RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.ForgotPasswordRequest>, AuthService.Validations.ForgotPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.VerifyEmailRequest>, AuthService.Validations.VerifyEmailRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.ResendOtpRequest>, AuthService.Validations.ResendOtpRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.RefreshTokenRequest>, AuthService.Validations.RefreshTokenRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.LogoutRequest>, AuthService.Validations.LogoutRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.ResetPasswordRequest>, AuthService.Validations.ResetPasswordRequestValidator>();
builder.Services.AddScoped<BookingService.IService, BookingService.Service>();
builder.Services.AddScoped<TransportPlanService.IService, TransportPlanService.Service>();
builder.Services.AddScoped<RoutePlanService.IService, RoutePlanService.Service>();
builder.Services.AddScoped<LocationService.IService, LocationService.Service>();
builder.Services.AddScoped<RouteService.IService, RouteService.Service>();
builder.Services.AddScoped<IValidator<BookingService.Request.CreateBookingRequest>, BookingService.Validations.CreateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.UpdateBookingRequest>, BookingService.Validations.UpdateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.CancelBookingRequest>, BookingService.Validations.CancelBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.RejectBookingRequest>, BookingService.Validations.RejectBookingRequestValidator>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<MediaService.IService, CloudinaryService.Service>();
builder.Services.AddScoped<Pegaxus.Service.DocumentStorageService.IService, Pegaxus.Service.DocumentStorageService.Service>();
builder.Services.AddScoped<MailService.IService, MailService.Service>();
builder.Services.AddScoped<AccountService.IService, AccountService.Service>();
builder.Services.AddScoped<HorseService.IService, HorseService.Service>();
builder.Services.AddScoped<EmployeeService.IService, EmployeeService.Service>();
builder.Services.AddScoped<ComplianceDossierService.IService, ComplianceDossierService.Service>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Pegaxus.Service.RouteChangeService.IService, Pegaxus.Service.RouteChangeService.Service>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.CreateRequest>, Pegaxus.Service.RouteChangeService.Validator.CreateRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.DecisionRequest>, Pegaxus.Service.RouteChangeService.Validator.DecisionRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.RouteChangeService.Request.ActivationRequest>, Pegaxus.Service.RouteChangeService.Validator.ActivationRequestValidator>();
builder.Services.AddScoped<Pegaxus.Service.PersonnelAssignmentService.IService, Pegaxus.Service.PersonnelAssignmentService.Service>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.PersonnelAssignmentService.Request.AssignRequest>, Pegaxus.Service.PersonnelAssignmentService.Validator.AssignRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.PersonnelAssignmentService.Request.ReplaceRequest>, Pegaxus.Service.PersonnelAssignmentService.Validator.ReplaceRequestValidator>();
builder.Services.AddScoped<Pegaxus.Service.VehicleService.IService, Pegaxus.Service.VehicleService.Service>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.ListRequest>, Pegaxus.Service.VehicleService.Validator.ListRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.CreateRequest>, Pegaxus.Service.VehicleService.Validator.CreateRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.UpdateRequest>, Pegaxus.Service.VehicleService.Validator.UpdateRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.VehicleService.Request.StatusRequest>, Pegaxus.Service.VehicleService.Validator.StatusRequestValidator>();
builder.Services.AddHostedService<Pegaxus.API.Services.MissingDocumentReminderWorker>();
builder.Services.AddHostedService<Pegaxus.API.Services.TransactionalOutboxWorker>();
builder.Services.AddScoped<NotificationService.IService, NotificationService.Service>();
builder.Services.AddScoped<NotificationService.INotificationRealtimeService, SignalRNotificationRealtimeService>();
builder.Services.AddScoped<IValidator<ComplianceDossierService.Request.InitializeRequest>, ComplianceDossierService.Validator.InitializeRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.NegotiateBookingRequest>, BookingService.Validations.NegotiateBookingRequestValidator>();
builder.Services.AddScoped<DossierTemplateService.IService, DossierTemplateService.Service>();
builder.Services.AddScoped<IValidator<DossierTemplateService.Request.ListRequest>, DossierTemplateService.Validator.ListRequestValidator>();
builder.Services.AddScoped<IValidator<DossierTemplateService.Request.SaveRequest>, DossierTemplateService.Validator.SaveRequestValidator>();
builder.Services.AddScoped<IValidator<DossierTemplateService.Request.ChangeStatusRequest>, DossierTemplateService.Validator.ChangeStatusRequestValidator>();
builder.Services.AddScoped<ComplianceRequirementService.IService, ComplianceRequirementService.Service>();
builder.Services.AddScoped<IValidator<ComplianceRequirementService.Request.ListRequest>, ComplianceRequirementService.Validator.ListRequestValidator>();
builder.Services.AddScoped<IValidator<ComplianceRequirementService.Request.SaveRequest>, ComplianceRequirementService.Validator.SaveRequestValidator>();
builder.Services.AddScoped<IValidator<ComplianceRequirementService.Request.StatusRequest>, ComplianceRequirementService.Validator.StatusRequestValidator>();
builder.Services.AddScoped<Pegaxus.Service.TransportProviderService.IService, Pegaxus.Service.TransportProviderService.Service>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.ListRequest>, Pegaxus.Service.TransportProviderService.Validator.ListRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.CreateRequest>, Pegaxus.Service.TransportProviderService.Validator.CreateRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.UpdateRequest>, Pegaxus.Service.TransportProviderService.Validator.UpdateRequestValidator>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.TransportProviderService.Request.StatusRequest>, Pegaxus.Service.TransportProviderService.Validator.StatusRequestValidator>();


builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
builder.Services.AddScoped<Pegaxus.Service.RouteOutsourcingService.IService, Pegaxus.Service.RouteOutsourcingService.Service>();
builder.Services.AddScoped<IValidator<Pegaxus.Service.RouteOutsourcingService.Request>, Pegaxus.Service.RouteOutsourcingService.Validator.RequestValidator>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await Pegaxus.API.AppDbContextSeedData.SeedAsync(db);
}

app.UseSwaggerAPI();

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("Frontend");
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/api/v1/auth/google/callback", StringComparison.OrdinalIgnoreCase))
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GoogleAuthentication");
        logger.LogInformation(
            "Google OAuth callback received. HasState={HasState}, CorrelationCookies={CorrelationCookies}",
            context.Request.Query.ContainsKey("state"),
            string.Join(", ", context.Request.Cookies.Keys
                .Where(key => key.StartsWith("pegaxus.google.correlation", StringComparison.Ordinal))));
    }

    await next();
});
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithSummary("Check deployment health.")
    .WithDescription("Reports application availability as JSON with status = healthy. Does not check databases or external services.");
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<Pegaxus.API.Hubs.ComplianceDossierHub>("/hubs/compliance-dossiers",
    options => options.CloseOnAuthenticationExpiration = true);

app.Run();

file sealed class PostgresEnumNameTranslator : INpgsqlNameTranslator
{
    private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();

    public string TranslateTypeName(string clrName) => _snakeCase.TranslateTypeName(clrName);

    public string TranslateMemberName(string clrName) => clrName;
}
