using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Multitenant.Application.Common;
using Multitenant.Application.Errors;
using Multitenant.Domain.Entities;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Converts every unhandled exception into an RFC 7807 problem+json response with a
/// stable machine-readable <c>code</c>, the correlation id and no stack traces
/// outside Development.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, code, detail) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.TraceIdentifier);
        }
        else
        {
            LogHandled(logger, statusCode, code, httpContext.TraceIdentifier);
        }

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = TypeFor(statusCode),
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = httpContext.TraceIdentifier,
                ["correlationId"] = httpContext.Items[CorrelationKeys.ItemKey]
            }
        };

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private (int StatusCode, string Title, string Code, string Detail) Map(Exception exception) =>
        exception switch
        {
            AppException app => (app.StatusCode, TitleFor(app.StatusCode), app.Code, app.Message),
            DomainException domain => (StatusCodes.Status400BadRequest, "Bad Request", "domain_error", domain.Message),
            BadHttpRequestException bad => (
                bad.StatusCode,
                "Bad Request",
                "invalid_request",
                "The request could not be parsed."),
            OperationCanceledException => (
                StatusCodes.Status499ClientClosedRequest,
                "Client Closed Request",
                "request_cancelled",
                "The request was cancelled by the client."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "internal_error",
                environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred. Quote the correlation id when contacting support."),
        };

    private static string TitleFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error",
    };

    private static string TypeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.10",
        StatusCodes.Status429TooManyRequests => "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
        _ => "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
    };

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing request {TraceId}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string traceId);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Warning,
        Message = "Request {TraceId} failed with {StatusCode} ({Code}).")]
    private static partial void LogHandled(ILogger logger, int statusCode, string code, string traceId);
}
