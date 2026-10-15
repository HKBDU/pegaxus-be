using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Pegaxus.API.Extensions;

public static class SwaggerExtensions
{
    public static void AddSwaggerServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo()
            {
                Title = "Pegaxus APIs", Version = "v1",
                Description = "Horse transport and compliance dossier APIs. Identifiers use UUIDs; dates use yyyy-MM-dd. " +
                    "Business responses use the isSuccess, code, message, and value envelope; errors include errorCode. " +
                    "File responses, OAuth redirects, and health checks do not use this envelope."
            });
            options.CustomSchemaIds(type => type.FullName!.Replace("+", "."));

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter the JWT token without the Bearer prefix."
            });

            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory,
                $"{typeof(SwaggerExtensions).Assembly.GetName().Name}.xml"));
            options.OperationFilter<AuthorizeOperationFilter>();
        });
    }

    public sealed class AuthorizeOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
    {
        public void Apply(OpenApiOperation operation,
            Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
        {
            var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
            var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();
            var allowsAnonymous = metadata.OfType<IAllowAnonymous>().Any();

            operation.Description += requiresAuthorization && !allowsAnonymous
                ? "\n\n**Authentication:** JWT Bearer. Missing or invalid tokens return 401; insufficient permissions return 403."
                : "\n\n**Authentication:** Not required.";
            var policies = metadata.OfType<IAuthorizeData>().Select(item => item.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy)).Distinct().ToArray();
            if (requiresAuthorization && !allowsAnonymous && policies.Length > 0)
                operation.Description += $"\n\n**Policy:** {string.Join(", ", policies)}.";

            if (!requiresAuthorization || allowsAnonymous)
            {
                return;
            }

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    }] = Array.Empty<string>()
                }
            };
        }
    }

    public static void UseSwaggerAPI(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            // foreach (var version in app.DescribeApiVersions().Select(version => version.GroupName))
            //     options.SwaggerEndpoint($"/swagger/{version}/swagger.json", version);

            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
            options.EnableFilter();
            options.DocExpansion(DocExpansion.List);
        });

        app.MapGet("/", () => Results.Redirect("/swagger/index.html"))
            .WithSummary("Open API documentation.")
            .WithDescription("Redirects to Swagger UI to browse documentation and try API requests.")
            .WithTags(string.Empty);
    }
}
