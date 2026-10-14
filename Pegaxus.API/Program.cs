using DotNetEnv;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pegaxus.API.Extensions;
using Pegaxus.API.Hubs;
using Pegaxus.API.Middleware;
using Pegaxus.Repository;

using CloudinaryService = Pegaxus.Service.CloudinaryService;

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

builder.Services.AddApiControllers();
builder.Services.AddFrontendCors();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(CloudinaryService.Service.PreviewHttpClient)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddRealtime();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddJwtServices(builder.Configuration);
builder.Services.AddGoogleSignIn(builder.Configuration);
builder.Services.AddSwaggerServices();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddErrorHandling();

builder.Services.AddAuthModule();
builder.Services.AddAccountsModule();
builder.Services.AddBookingModule();
builder.Services.AddInvoicingModule(builder.Configuration);
builder.Services.AddTransportPlanningModule();
builder.Services.AddFleetModule();
builder.Services.AddComplianceModule();
builder.Services.AddMessagingModule();

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
app.MapHub<ComplianceDossierHub>("/hubs/compliance-dossiers",
    options => options.CloseOnAuthenticationExpiration = true);

app.Run();
