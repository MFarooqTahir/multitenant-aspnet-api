using Multitenant.Application.Common;
using Serilog.Context;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Accepts an inbound <c>X-Correlation-Id</c> (or generates one), stores it on the
/// request, echoes it on the response and pushes it into the Serilog log context.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationKeys.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > MaxLength)
        {
            correlationId = context.TraceIdentifier;
        }

        correlationId = correlationId.Trim();
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
}