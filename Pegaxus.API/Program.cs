using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;


using JwtService = Pegaxus.Service.JwtService;
using MediaService = Pegaxus.Service.MediaService;
using CloudinaryService = Pegaxus.Service.CloudinaryService;


Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// builder.Services.AddApplicationServices();
builder.Services.AddJwtServices(builder.Configuration);
builder.Services.AddSwaggerServices();

builder.Services.AddScoped<JwtService.IJwtService, JwtService.JwtServices>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<MediaService.IService, CloudinaryService.Service>();


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

app.MapControllers();

app.Run();