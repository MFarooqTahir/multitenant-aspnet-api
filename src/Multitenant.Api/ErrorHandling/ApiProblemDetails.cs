using Multitenant.Application.Common;

namespace Multitenant.Api.ErrorHandling;

/// <summary>
/// Builds the RFC 7807 responses used across the API. Centralising the shape means
/// the exception handler, status-code pages and the rate limiter cannot drift.
/// </summary>
public static class ApiProblemDetails
{
    /// <summary>Creates a problem with the standard <c>code</c>, <c>traceId</c> and <c>correlationId</c>.</summary>
    public static Microsoft.AspNetCore.Mvc.ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string code,
        string? detail = null)
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = TypeFor(statusCode),
            Detail = detail ?? title,
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier,
                ["correlationId"] = context.Items[CorrelationKeys.ItemKey],
            },
        };

        return problem;
    }

    /// <summary>
    /// Creates a problem for a framework-generated status code, or returns
    /// <c>null</c> when that status should not be rewritten.
    /// </summary>
    public static Microsoft.AspNetCore.Mvc.ProblemDetails? CreateForStatus(HttpContext context, int statusCode)
    {
        var (title, code) = statusCode switch
        {
            StatusCodes.Status401Unauthorized => ("Unauthorized", "unauthorized"),
            StatusCodes.Status403Forbidden => ("Forbidden", "forbidden"),
            StatusCodes.Status404NotFound => ("Not Found", "not_found"),
            StatusCodes.Status405MethodNotAllowed => ("Method Not Allowed", "method_not_allowed"),
            _ => (string.Empty, string.Empty),
        };

        return string.IsNullOrEmpty(code)
            ? null
            : Create(context, statusCode, title, code);
    }

    public static string TypeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
        StatusCodes.Status405MethodNotAllowed => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.6",
        StatusCodes.Status409Conflict => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
        StatusCodes.Status429TooManyRequests => "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
        _ => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
    };
}
