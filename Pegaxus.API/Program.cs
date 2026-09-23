using DotNetEnv;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using System.Text.Json.Serialization;
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


Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
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
                "The request data is invalid"));
        };
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
builder.Services.AddSwaggerServices();
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");

builder.Services.AddScoped<JwtService.IJwtService, JwtService.JwtServices>();
builder.Services.AddScoped<AuthService.IService, AuthService.Service>();
builder.Services.AddScoped<IValidator<AuthService.Request.RegisterRequest>, AuthService.Validations.RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<AuthService.Request.ForgotPasswordRequest>, AuthService.Validations.ForgotPasswordRequestValidator>();
builder.Services.AddScoped<BookingService.IService, BookingService.Service>();
builder.Services.AddScoped<IValidator<BookingService.Request.CreateBookingRequest>, BookingService.Validations.CreateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.UpdateBookingRequest>, BookingService.Validations.UpdateBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.CancelBookingRequest>, BookingService.Validations.CancelBookingRequestValidator>();
builder.Services.AddScoped<IValidator<BookingService.Request.RejectBookingRequest>, BookingService.Validations.RejectBookingRequestValidator>();
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddScoped<MediaService.IService, CloudinaryService.Service>();
builder.Services.AddScoped<MailService.IService, MailService.Service>();
builder.Services.AddScoped<AccountService.IService, AccountService.Service>();


builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSwaggerAPI();

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

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
