using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Swashbuckle.AspNetCore.Swagger;

namespace Pegaxus.Service.Test;

public class SwaggerDocumentationTests
{
    [Theory]
    [InlineData("UploadDocument")]
    [InlineData("SubmitDocumentVersion")]
    public void DocumentSubmission_AcceptsJsonAssetReferencesInsteadOfMultipart(string actionName)
    {
        var action = typeof(ComplianceDossiersController).GetMethod(actionName)!;
        Assert.Equal("application/json", Assert.Single(action.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes));
        var request = action.GetParameters().Single(parameter => parameter.Name == "request");
        Assert.NotNull(request.GetCustomAttribute<FromBodyAttribute>());
        Assert.Null(request.GetCustomAttribute<FromFormAttribute>());
        Assert.Equal(typeof(List<Pegaxus.Service.UploadService.Request.UploadedAssetReference>),
            request.ParameterType.GetProperty("Files")!.PropertyType);
    }

    [Fact]
    public void AllControllerEndpoints_HaveSummaryDescriptionAndAuthenticationNotes()
    {
        using var app = CreateApp();
        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.NotEmpty(document.Paths);
        foreach (var path in document.Paths)
        foreach (var endpoint in path.Value.Operations)
        {
            var operation = endpoint.Value;
            Assert.True(!string.IsNullOrWhiteSpace(operation.Summary), $"Missing summary: {endpoint.Key} {path.Key}");
            Assert.True(!string.IsNullOrWhiteSpace(operation.Description), $"Missing description: {endpoint.Key} {path.Key}");
            Assert.Contains("Authentication:", operation.Description);
            Assert.Contains(operation.Security.Count > 0 ? "JWT Bearer" : "Not required", operation.Description);
            Assert.DoesNotMatch(@"[^\u0000-\u007F]", operation.Summary + operation.Description);
        }
    }

    [Theory]
    [InlineData("/api/Health", OperationType.Get, "Check API health.")]
    [InlineData("/api/v1/transport-plans/{transportPlanId}/assignments", OperationType.Get, "Get transport plan personnel assignments.")]
    [InlineData("/api/v1/quarantine-proofs/{proofId}/finalize", OperationType.Patch, "Finalize quarantine verification.")]
    [InlineData("/api/v1/customs-clearance-proofs/{proofId}/finalize", OperationType.Patch, "Finalize customs clearance.")]
    [InlineData("/api/v1/bookings/{bookingId}", OperationType.Patch, "Update a booking.")]
    [InlineData("/api/v1/bookings/{bookingId}/negotiated-status", OperationType.Patch, "Record a negotiated booking status.")]
    [InlineData("/api/v1/bookings/{bookingId}/dossier-templates", OperationType.Get, "Suggest dossier templates for a booking.")]
    [InlineData("/api/v1/transports/{transportId}/compliance-dossier/preview", OperationType.Post, "Preview a compliance dossier.")]
    [InlineData("/api/v1/bookings/{bookingId}/compliance-dossier", OperationType.Post, "Initialize a compliance dossier.")]
    [InlineData("/api/v1/compliance-dossiers/{dossierId}/requirements", OperationType.Post, "Add a dossier requirement manually.")]
    [InlineData("/api/v1/compliance-dossiers/{dossierId}/requirements/{requirementId}/waive", OperationType.Patch, "Waive a dossier requirement.")]
    public void EndpointSummary_DescribesTheCorrectAction(string path, OperationType method, string summary)
    {
        using var app = CreateApp();
        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.Equal(summary, document.Paths[path].Operations[method].Summary);
    }

    [Fact]
    public void AllControllerActions_HaveCompleteXmlDocumentation()
    {
        var assembly = typeof(HealthController).Assembly;
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml"));
        var members = document.Descendants("member").ToArray();
        var actions = assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods()).Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var name = $"M:{action.DeclaringType!.FullName}.{action.Name}";
            var member = Assert.Single(members.Where(item => (string?)item.Attribute("name") == name ||
                ((string?)item.Attribute("name"))!.StartsWith(name + "(", StringComparison.Ordinal)));
            Assert.False(string.IsNullOrWhiteSpace(member.Element("summary")?.Value), name);
            Assert.False(string.IsNullOrWhiteSpace(member.Element("returns")?.Value), name);
            Assert.Equal(action.GetParameters().Select(parameter => parameter.Name),
                member.Elements("param").Select(parameter => (string?)parameter.Attribute("name")));
            Assert.All(member.Elements("param"), parameter => Assert.False(string.IsNullOrWhiteSpace(parameter.Value), name));
            Assert.All(member.Elements("exception"), exception =>
            {
                Assert.StartsWith("T:", (string?)exception.Attribute("cref"));
                Assert.False(string.IsNullOrWhiteSpace(exception.Value), name);
            });
        }
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HealthController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Services.AddControllers();
        builder.Services.AddSwaggerServices();
        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
