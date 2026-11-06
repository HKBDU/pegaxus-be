using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.API.Middleware;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.DocumentStorageService;
using Pegaxus.Service.JwtService;
using Pegaxus.Service.MediaService;

namespace Pegaxus.Service.Test;

public class UploadsConfirmHttpTests
{
    [Theory]
    [InlineData("avatars/", "avatar")]
    [InlineData("evidence/", "attachment")]
    public async Task Confirm_RoutesAvatarPublicIdsToAvatarConfirmation(string prefix, string expected)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, _) = await ProofUploadTests.SeedAsync(db);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "uploads-tests", ["JwtOptions:Audience"] = "uploads-tests"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(UploadsController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddSingleton<IMediaService, ConfirmMedia>();
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        await using var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(app.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Role, JwtServices.GetRoleName(EmployeeRole.DRIVER))]));

        using var response = await client.PostAsJsonAsync("/api/v1/uploads/confirm", new { publicId = prefix + "x" });

        var body = await response.Content.ReadAsStringAsync();
        var value = JsonDocument.Parse(body).RootElement.GetProperty("value"); Assert.True(value.ValueKind == JsonValueKind.Object, body);
        Assert.Equal(expected, value.GetProperty("fileName").GetString());
    }

    private sealed class ConfirmMedia : IMediaService
    {
        public Task<ConfirmedUploadResult> ConfirmAvatarAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfirmedUploadResult(publicId, "https://res.cloudinary.com/t/image/upload/v1/" + publicId + ".png", "avatar", "image/png", 10));
        public Task<ConfirmedUploadResult> ConfirmUploadAsync(Guid accountId, string publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfirmedUploadResult(publicId, "https://res.cloudinary.com/t/raw/authenticated/v1/" + publicId, "attachment", "application/pdf", 10));
        public Task<SignedDocumentUrl> CreateReadUrlAsync(string fileUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SignedDocumentUrl(fileUrl, DateTimeOffset.UtcNow.AddMinutes(5)));
        public Task<PreviewFile> DownloadPreviewAsync(string fileUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> UploadPrivateImageAsync(Guid imageId, PreviewFile image, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePrivateImageAsync(Guid imageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SignedUploadResult> CreateSignedUploadAsync(Guid accountId, string entity, string contentType, long sizeBytes) => throw new NotSupportedException();
    }
}
