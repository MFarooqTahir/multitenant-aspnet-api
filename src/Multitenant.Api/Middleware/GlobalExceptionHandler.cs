using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Multitenant.Api.ErrorHandling;
using Multitenant.Application.Errors;
using Multitenant.Domain.Entities;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 responses with a stable
/// machine-readable <c>code</c>. Stack traces are only exposed in Development.
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

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = ApiProblemDetails.Create(httpContext, statusCode, title, code, detail),
        });
    }

    private (int StatusCode, string Title, string Code, string Detail) Map(Exception exception) =>
        exception switch
        {
            AppException app => (app.StatusCode, TitleFor(app.StatusCode), app.Code, app.Message),
            DomainException domain => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "domain_error",
                domain.Message),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflict",
                "concurrency_conflict",
                "The resource was changed by another request. Reload it and try again."),
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                TitleFor(badRequest.StatusCode),
                badRequest.StatusCode switch
                {
                    StatusCodes.Status413PayloadTooLarge => "payload_too_large",
                    StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
                    _ => "invalid_request",
                },
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
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Payload Too Large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported Media Type",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status499ClientClosedRequest => "Client Closed Request",
        _ => "Internal Server Error",
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
