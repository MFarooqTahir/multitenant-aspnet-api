using Microsoft.AspNetCore.Mvc;
using Multitenant.Api.ErrorHandling;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Gives framework-generated 401/403/404/405 responses (which have no body) the
/// same problem+json shape as application errors.
/// </summary>
public static class ProblemDetailsStatusCodePagesExtensions
{
    public static IApplicationBuilder UseProblemDetailsStatusCodePages(this IApplicationBuilder app) =>
        app.UseStatusCodePages(async context =>
        {
            var httpContext = context.HttpContext;
            var response = httpContext.Response;

            if (response.HasStarted || response.ContentLength > 0)
            {
                return;
            }

            var problem = ApiProblemDetails.CreateForStatus(httpContext, response.StatusCode);
            if (problem is null)
            {
                return;
            }

            response.ContentType = "application/problem+json";
            await response.WriteAsJsonAsync(
                problem,
                problem.GetType(),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
        });
}
