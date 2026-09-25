using DotNetEnv;
using Microsoft.AspNetCore.Authentication.Google;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using Pegaxus.Repository.Entities;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.Models;

using AuthService = Pegaxus.Service.Auth;
using BookingService = Pegaxus.Service.BookingService;
using JwtService = Pegaxus.Service.JwtService;
using MediaService = Pegaxus.Service.MediaService;
using CloudinaryService = Pegaxus.Service.CloudinaryService;
using MailService = Pegaxus.Service.MailService;
using AccountService = Pegaxus.Service.AccountService;
using HorseService = Pegaxus.Service.HorseService;


Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Keep issued access tokens at the 15-minute API contract lifetime in every environment.
builder.Configuration["JwtOptions:ExpirationMinutes"] = "15";

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    })
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
            options.Scope.Add("email");
            options.Scope.Add("profile");
            options.SaveTokens = false;
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
builder.Services.AddScoped<IValidator<BookingService.Request.CreateBookingRequest>, BookingService.Validations.CreateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.UpdateBookingRequest>, BookingService.Validations.UpdateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.CancelBookingRequest>, BookingService.Validations.CancelBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.RejectBookingRequest>, BookingService.Validations.RejectBookingRequestValidator>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<MediaService.IService, CloudinaryService.Service>();
builder.Services.AddScoped<MailService.IService, MailService.Service>();
builder.Services.AddScoped<AccountService.IService, AccountService.Service>();
builder.Services.AddScoped<HorseService.IService, HorseService.Service>();


builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSwaggerAPI();

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();

app.Run();

file sealed class PostgresEnumNameTranslator : INpgsqlNameTranslator
{
    private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();

    public string TranslateTypeName(string clrName) => _snakeCase.TranslateTypeName(clrName);

    public string TranslateMemberName(string clrName) => clrName;
}
