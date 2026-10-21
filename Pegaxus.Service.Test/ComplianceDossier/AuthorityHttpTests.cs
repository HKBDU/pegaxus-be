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

public class AuthorityHttpTests
{
    [Fact]
    public async Task AuthorityEndpoints_AcceptJsonPublicIdsAndRejectMultipart()
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, dossierId) = await AuthorityPresignedFileTests.SeedAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, specialist);

        var submitUrl = $"/api/v1/compliance-dossiers/{dossierId}/authority-submissions";
        using var created = await client.PostAsJsonAsync(submitUrl, new
        {
            authorityName = "Authority", submissionReference = "REF-HTTP", submittedAt = DateTimeOffset.UtcNow,
            proofFile = DocumentUploadTests.File("proof.pdf", "application/pdf")
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var submissionId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("authoritySubmissionId").GetGuid();

        var decisionUrl = $"/api/v1/authority-submissions/{submissionId}/decision";
        using var decided = await client.PatchAsJsonAsync(decisionUrl, new
        {
            decision = "APPROVED", permitNumber = "PERMIT-1", decisionFile = DocumentUploadTests.File("permit.pdf", "application/pdf")
        });
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);

        using var multipart = new MultipartFormDataContent { { new StringContent("Authority"), "authorityName" } };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(submitUrl, multipart)).StatusCode);
        using var decisionForm = new MultipartFormDataContent { { new StringContent("APPROVED"), "decision" } };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PatchAsync(decisionUrl, decisionForm)).StatusCode);
    }

    [Theory]
    [InlineData("quarantine-proofs")]
    [InlineData("customs-clearance-proofs")]
    public async Task FinalizeEndpoints_AcceptJsonAndRejectMultipart(string proofs)
    {
        await using var db = TestSupport.CreateDb();
        var (specialist, _) = await AuthorityPresignedFileTests.SeedAsync(db);
        await using var host = await StartAsync(db);
        using var client = Client(host, specialist);
        var url = $"/api/v1/{proofs}/{Guid.NewGuid()}/finalize";

        using var json = await client.PatchAsJsonAsync(url, new { action = "REJECT_PROOF", rejectionReason = "Blurry" });
        Assert.Equal(HttpStatusCode.NotFound, json.StatusCode);
        using var form = new MultipartFormDataContent { { new StringContent("REJECT_PROOF"), "action" } };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PatchAsync(url, form)).StatusCode);
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32), ["JwtOptions:ExpirationMinutes"] = "15",
            ["JwtOptions:Issuer"] = "authority-tests", ["JwtOptions:Audience"] = "authority-tests"
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
                new Claim(ClaimTypes.Role, JwtServices.GetRoleName(EmployeeRole.TRANSPORT_SPECIALIST))]));
        return client;
    }
}
