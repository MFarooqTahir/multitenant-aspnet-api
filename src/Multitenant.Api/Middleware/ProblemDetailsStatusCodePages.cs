using Microsoft.AspNetCore.Mvc;
using Multitenant.Application.Common;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Turns framework-generated 401/403/404/405 responses (which have no body) into
/// RFC 7807 problem+json, so every error the API returns has the same shape.
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

            var statusCode = response.StatusCode;
            var (title, type, code) = statusCode switch
            {
                StatusCodes.Status401Unauthorized => (
                    "Unauthorized",
                    "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
                    "unauthorized"),
                StatusCodes.Status403Forbidden => (
                    "Forbidden",
                    "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
                    "forbidden"),
                StatusCodes.Status404NotFound => (
                    "Not Found",
                    "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
                    "not_found"),
                StatusCodes.Status405MethodNotAllowed => (
                    "Method Not Allowed",
                    "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.6",
                    "method_not_allowed"),
                _ => (string.Empty, string.Empty, string.Empty),
            };

            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Type = type,
                Detail = title,
                Instance = httpContext.Request.Path,
                Extensions =
                {
                    ["code"] = code,
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["correlationId"] = httpContext.Items[CorrelationKeys.ItemKey],
                },
            };

            response.ContentType = "application/problem+json";
            await response.WriteAsJsonAsync(
                problem,
                problem.GetType(),
                options: null,
                contentType: "application/problem+json",
                cancellationToken: httpContext.RequestAborted);
        });
}
