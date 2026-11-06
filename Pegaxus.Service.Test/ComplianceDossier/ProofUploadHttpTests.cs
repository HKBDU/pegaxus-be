using System.Net;
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
using Pegaxus.Repository;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.ComplianceDossierService;
using Pegaxus.Service.JwtService;

namespace Pegaxus.Service.Test;

public class ProofUploadHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_AcceptsJsonReferences_ReturnsCreated_RejectsMultipart(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await ProofUploadTests.SeedAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, accountId);
        var url = $"/api/v1/compliance-dossiers/{dossierId}/{(customs ? "customs-clearance-proofs" : "quarantine-proofs")}";
        var file = DocumentUploadTests.File("stamp.png", "image/png");
        using var result = await client.PostAsJsonAsync(url, new { bcpName = "BCP", customsPortName = "Port", files = new[] { file } });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isSuccess").GetBoolean());
        Assert.NotEqual(Guid.Empty, body.GetProperty("value").GetProperty(customs ? "customsClearanceProofId" : "quarantineProofId").GetGuid());
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("BCP"), "bcpName");
        using var rejected = await client.PostAsync(url, multipart);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_NullFilesReturns400_AnonymousReturns401(bool customs)
    {
        await using var db = TestSupport.CreateDb();
        var (accountId, dossierId) = await ProofUploadTests.SeedAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, accountId);
        var url = $"/api/v1/compliance-dossiers/{dossierId}/{(customs ? "customs-clearance-proofs" : "quarantine-proofs")}";
        using var result = await client.PostAsJsonAsync(url, new { bcpName = "BCP", customsPortName = "Port", files = (object?)null });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.PostAsJsonAsync(url, new { bcpName = "BCP" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "proof-tests", ["JwtOptions:Audience"] = "proof-tests"
        });
        builder.Services.AddSingleton(db);
        builder.Services.AddApiControllers();
        builder.Services.AddControllers().AddApplicationPart(typeof(ComplianceDossiersController).Assembly);
        builder.Services.AddJwtServices(builder.Configuration);
        builder.Services.AddSingleton<IComplianceDossierService>(DocumentUploadTests.CreateService(db, new DocumentUploadTests.RecordingStorage()));
        builder.Services.AddTransient<GlobalExceptionHandlerMiddleware>();
        var app = builder.Build();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication host, Guid accountId)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtServices(host.Configuration).GenerateAccessToken(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString()), new Claim(ClaimTypes.Role, "Employee"),
                new Claim(ClaimTypes.Role, JwtServices.GetRoleName(EmployeeRole.DRIVER))]));
        return client;
    }
}
