using System.Text.RegularExpressions;
using Multitenant.Application.Common;
using Serilog.Context;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Accepts an inbound <c>X-Correlation-Id</c> (or generates one), stores it on the
/// request, echoes it on the response and pushes it into the Serilog log context.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationKeys.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId)
            || correlationId.Length > MaxLength
            || !SafePattern().IsMatch(correlationId.Trim()))
        {
            correlationId = context.TraceIdentifier;
        }
        else
        {
            correlationId = correlationId.Trim();
        }

        context.Items[CorrelationKeys.ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationKeys.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Whitelist: only safe characters that cannot inject CRLF or other control sequences
    // into log files, headers, or downstream consumers.
    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafePattern();
}
