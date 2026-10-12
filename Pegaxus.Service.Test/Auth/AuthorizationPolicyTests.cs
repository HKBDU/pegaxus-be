using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pegaxus.API.Controllers;
using Pegaxus.API.Extensions;
using Pegaxus.Repository.Enum;
using Pegaxus.Service.JwtService;

namespace Pegaxus.Service.Test;

public class AuthorizationPolicyTests
{
    [Fact]
    public void DocumentUpload_RequiresCustomerPolicy()
    {
        var action = typeof(ComplianceDossiersController).GetMethod("UploadDocument");
        Assert.NotNull(action);
        Assert.Equal(JwtExtensions.CustomerPolicy, action.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Theory]
    [InlineData("SubmitDocumentVersion", JwtExtensions.CustomerPolicy)]
    [InlineData("UpdateDocumentMetadata", JwtExtensions.CustomerPolicy)]
    [InlineData("GetDocumentVersions", JwtExtensions.ComplianceDossierViewerPolicy)]
    public void DocumentVersionActions_RequireScopedPolicies(string actionName, string policy)
    {
        var action = typeof(ComplianceDossiersController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.Equal(policy, action.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    [Theory]
    [InlineData("ApproveDocument")]
    [InlineData("RevokeDocumentReview")]
    [InlineData("MarkReadyForSubmission")]
    public void DocumentReviewActions_RequireTransportSpecialistPolicy(string actionName)
    {
        var action = typeof(ComplianceDossiersController).GetMethod(actionName);
        Assert.NotNull(action);
        Assert.Equal(JwtExtensions.TransportSpecialistPolicy, action.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    internal static ServiceProvider CreateProvider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtOptions:SecretKey"] = new string('x', 32),
            ["JwtOptions:ExpirationMinutes"] = "15"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwtServices(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("AddRequirement", "POST", "{dossierId:guid}/requirements")]
    [InlineData("UpdateRequirement", "PATCH", "{dossierId:guid}/requirements/{requirementId:guid}")]
    [InlineData("RemoveRequirement", "DELETE", "{dossierId:guid}/requirements/{requirementId:guid}")]
    [InlineData("GetMissingRequirements", "GET", "{dossierId:guid}/missing-requirements")]
    [InlineData("RequestMissingDocuments", "POST", "{dossierId:guid}/missing-document-reminders")]
    public async Task DossierRequirementActions_UseTransportSpecialistPolicy(string methodName, string verb, string route)
    {
        var method = typeof(ComplianceDossiersController).GetMethod(methodName);
        Assert.NotNull(method);
        var endpoint = Assert.Single(method.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(route, endpoint.Template);
        Assert.Contains(verb, endpoint.HttpMethods);
        var policy = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Policy;
        Assert.Equal(JwtExtensions.TransportSpecialistPolicy, policy);
        using var provider = CreateProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        foreach (var role in new[] { "TransportSpecialist", "Customer", "LogisticsManager", "FleetRouteCoordinator", "Driver", "Escort" })
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
            Assert.Equal(role == "TransportSpecialist", (await authorization.AuthorizeAsync(user, null, policy!)).Succeeded);
        }
    }

    [Theory]
    [InlineData("RejectDocument", JwtExtensions.TransportSpecialistPolicy)]
    [InlineData("AddDocumentRequestComment", JwtExtensions.CustomerOrTransportSpecialistPolicy)]
    [InlineData("GetDocumentRequestComments", JwtExtensions.CustomerOrTransportSpecialistPolicy)]
    public void DocumentRequestActions_UseExpectedPolicies(string action, string policy)
    {
        var method = typeof(ComplianceDossiersController).GetMethod(action);
        Assert.NotNull(method);
        Assert.Equal(policy, Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    [Fact]
    public async Task Controllers_UseRegisteredPoliciesInsteadOfInlineRoles()
    {
        using var provider = CreateProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var controllers = typeof(ComplianceDossiersController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type));
        foreach (var controller in controllers)
        {
            var attributes = controller.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(controller.GetMethods().SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>()));
            foreach (var attribute in attributes)
            {
                Assert.True(string.IsNullOrEmpty(attribute.Roles), controller.Name);
                if (attribute.Policy is not null)
                    Assert.NotNull(await policies.GetPolicyAsync(attribute.Policy));
            }
        }
    }

    [Fact]
    public async Task Policies_AllowOnlyTheirExpectedRoles()
    {
        using var provider = CreateProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var allowedRoles = new Dictionary<string, string[]>
        {
            [JwtExtensions.CustomerPolicy] = ["Customer"],
            [JwtExtensions.EmployeePolicy] = ["Employee"],
            [JwtExtensions.LogisticsManagerPolicy] = ["LogisticsManager"],
            [JwtExtensions.TransportSpecialistPolicy] = ["TransportSpecialist"],
            [JwtExtensions.FleetRouteCoordinatorPolicy] = ["FleetRouteCoordinator"],
            [JwtExtensions.DriverPolicy] = ["Driver"],
            [JwtExtensions.EscortPolicy] = ["Escort"],
            [JwtExtensions.CustomerOrEmployeePolicy] = ["Customer", "Employee"],
            [JwtExtensions.InvoiceViewerPolicy] = ["Customer", "LogisticsManager"],
            [JwtExtensions.CustomerOrTransportSpecialistPolicy] = ["Customer", "TransportSpecialist"],
            [JwtExtensions.ComplianceDossierViewerPolicy] = ["Customer", "TransportSpecialist", "LogisticsManager"],
            ["TransportSpecialistOrLogisticsManagerPolicy"] = ["TransportSpecialist", "LogisticsManager"],
            ["DriverOrEscortPolicy"] = ["Driver", "Escort"]
        };
        string[] roles = ["Customer", "Employee", "LogisticsManager", "TransportSpecialist", "FleetRouteCoordinator", "Driver", "Escort", "Admin"];
        foreach (var (policyName, expectedRoles) in allowedRoles)
        {
            var policy = await policies.GetPolicyAsync(policyName);
            Assert.NotNull(policy);
            Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, policy)).Succeeded);
            foreach (var role in roles)
            {
                var claims = new List<Claim> { new(ClaimTypes.Role, role) };
                if (role is not ("Customer" or "Employee" or "Admin"))
                    claims.Add(new Claim(ClaimTypes.Role, "Employee"));
                var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
                var expected = expectedRoles.Contains(role) || expectedRoles.Contains("Employee") && user.IsInRole("Employee");
                Assert.Equal(expected, (await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
            }
        }
    }

    [Theory]
    [InlineData(EmployeeRole.LOGISTICS_MANAGER, "LogisticsManager")]
    [InlineData(EmployeeRole.TRANSPORT_SPECIALIST, "TransportSpecialist")]
    [InlineData(EmployeeRole.FLEET_ROUTE_COORDINATOR, "FleetRouteCoordinator")]
    [InlineData(EmployeeRole.DRIVER, "Driver")]
    [InlineData(EmployeeRole.ESCORT, "Escort")]
    public void EmployeeRoleNames_PreserveExistingJwtClaims(EmployeeRole role, string expected) =>
        Assert.Equal(expected, JwtServices.GetRoleName(role));

    [Theory]
    [InlineData(AccountType.CUSTOMER, "Customer")]
    [InlineData(AccountType.EMPLOYEE, "Employee")]
    public void AccountRoleNames_PreserveExistingJwtClaims(AccountType accountType, string expected) =>
        Assert.Equal(expected, JwtServices.GetRoleName(accountType));

    [Fact]
    public void UnknownRoles_AreRejected()
    {
        Assert.Throws<Pegaxus.Service.Exceptions.Exceptions>(() => JwtServices.GetRoleName((AccountType)0));
        Assert.Throws<Pegaxus.Service.Exceptions.Exceptions>(() => JwtServices.GetRoleName((EmployeeRole)0));
    }

    [Fact]
    public void Catalog_ReadAndWriteActionsHaveSeparatePolicies()
    {
        var controller = typeof(ComplianceRequirementsController);
        foreach (var method in controller.GetMethods().Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any()))
        {
            var expectedPolicy = method.IsDefined(typeof(HttpGetAttribute))
                ? "TransportSpecialistOrLogisticsManagerPolicy"
                : JwtExtensions.TransportSpecialistPolicy;
            Assert.Equal(expectedPolicy, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        }
    }
}
