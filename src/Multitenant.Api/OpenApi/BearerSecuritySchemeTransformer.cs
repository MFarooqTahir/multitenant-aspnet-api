using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Multitenant.Api.OpenApi;

/// <summary>
/// Declares the bearer scheme in the OpenAPI document so Scalar can show an
/// authentication panel and generated clients know how to authenticate.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token returned by POST /api/v1/auth/login.",
        };

        return Task.CompletedTask;
    }
}
