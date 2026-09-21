using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;
using CloudinaryService = Pegaxus.Service.CloudinaryService;
using JwtService = Pegaxus.Service.JwtService;
using MediaService = Pegaxus.Service.MediaService;

Env.Load();

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? $"Host=localhost;Port={builder.Configuration["POSTGRES_PORT"] ?? "5432"};Database={builder.Configuration["POSTGRES_DB"] ?? "pegaxus_db"};Username={builder.Configuration["POSTGRES_USER"] ?? "pegaxus_user"};Password={builder.Configuration["POSTGRES_PASSWORD"] ?? "pegaxus_secret_password"}";

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddJwtServices(builder.Configuration);
builder.Services.AddSwaggerServices();
builder.Services.AddScoped<JwtService.IJwtService, JwtService.JwtServices>();
builder.Services.AddScoped<MediaService.IService, CloudinaryService.Service>();
builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? builder.Configuration["ALLOWED_ORIGINS"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ["http://localhost:3000", "http://localhost:5173"];

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    app.UseSwaggerApi();

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
