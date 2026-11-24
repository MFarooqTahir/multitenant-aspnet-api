using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Multitenant.Application.Common;

namespace Multitenant.Api.ErrorHandling;

/// <summary>
/// Builds the RFC 7807 responses used across the API. Centralising the shape means
/// the exception handler, status-code pages, validation filter, model-binding
/// errors and the rate limiter cannot drift.
/// </summary>
public static class ApiProblemDetails
{
    /// <summary>Creates a problem with the standard <c>code</c>, <c>traceId</c> and <c>correlationId</c>.</summary>
    public static ProblemDetails Create(
        HttpContext context,
        int statusCode,
        string title,
        string code,
        string? detail = null) =>
        new()
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

    /// <summary>
    /// Creates the 400 response used for both FluentValidation failures and
    /// model-binding failures, so every validation error carries the same
    /// <c>code</c>, <c>traceId</c> and <c>correlationId</c>.
    /// </summary>
    public static ValidationProblemDetails CreateValidation(
        HttpContext context,
        IReadOnlyDictionary<string, string[]> errors,
        string detail = "One or more validation errors occurred.") =>
        new(errors.ToDictionary(x => x.Key, x => x.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = TypeFor(StatusCodes.Status400BadRequest),
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = "validation_failed",
                ["traceId"] = context.TraceIdentifier,
                ["correlationId"] = context.Items[CorrelationKeys.ItemKey],
            },
        };

    /// <summary>
    /// Turns MVC model-state errors (malformed JSON, unparsable query values) into
    /// the same validation problem the FluentValidation filter produces. These
    /// failures happen before the action filter runs, so without this they would
    /// return a body with no machine-readable <c>code</c>.
    /// </summary>
    public static ValidationProblemDetails FromModelState(
        HttpContext context,
        ModelStateDictionary modelState)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, entry) in modelState)
        {
            if (entry.Errors.Count == 0)
            {
                continue;
            }

            var messages = entry.Errors
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "The value is invalid."
                    : error.ErrorMessage)
                .ToArray();

            var path = string.IsNullOrWhiteSpace(key) ? "request" : ToCamelCasePath(key);
            errors[path] = errors.TryGetValue(path, out var existing)
                ? [.. existing, .. messages]
                : messages;
        }

        return CreateValidation(context, errors);
    }

    /// <summary>
    /// Creates a problem for a framework-generated status code, or returns
    /// <c>null</c> when that status should not be rewritten.
    /// </summary>
    public static ProblemDetails? CreateForStatus(HttpContext context, int statusCode)
    {
        var (title, code) = StatusDetails(statusCode);

        return string.IsNullOrEmpty(code)
            ? null
            : Create(context, statusCode, title, code);
    }

    public static string? CodeForStatus(int statusCode) => StatusDetails(statusCode).Code;

    private static (string Title, string Code) StatusDetails(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ("Bad Request", "invalid_request"),
        StatusCodes.Status401Unauthorized => ("Unauthorized", "unauthorized"),
        StatusCodes.Status403Forbidden => ("Forbidden", "forbidden"),
        StatusCodes.Status404NotFound => ("Not Found", "not_found"),
        StatusCodes.Status405MethodNotAllowed => ("Method Not Allowed", "method_not_allowed"),
        StatusCodes.Status413PayloadTooLarge => ("Payload Too Large", "payload_too_large"),
        StatusCodes.Status415UnsupportedMediaType => ("Unsupported Media Type", "unsupported_media_type"),
        StatusCodes.Status429TooManyRequests => ("Too Many Requests", "rate_limited"),
        StatusCodes.Status500InternalServerError => ("Internal Server Error", "internal_error"),
        _ => (string.Empty, string.Empty),
    };

    /// <summary>Maps a status code to the RFC 9110 (or RFC 6585) section that defines it.</summary>
    public static string TypeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
        StatusCodes.Status405MethodNotAllowed => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.6",
        StatusCodes.Status409Conflict => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
        StatusCodes.Status413PayloadTooLarge => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.14",
        StatusCodes.Status415UnsupportedMediaType => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.16",
        StatusCodes.Status429TooManyRequests => "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
        _ => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
    };

    /// <summary>Converts a model-state key to the camelCase form used in JSON errors.</summary>
    public static string ToCamelCasePath(string propertyName) =>
        string.Join(
            '.',
            propertyName.Split('.').Select(segment =>
                segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
