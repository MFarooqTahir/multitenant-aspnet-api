using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration;

namespace Multitenant.Api.RateLimiting;

/// <summary>
/// Two-tier in-process rate limiting:
/// <list type="bullet">
/// <item><c>tenant</c> (global) — 120 requests/minute per tenant, falling back to the
/// client IP for anonymous traffic.</item>
/// <item><c>auth</c> — 10 requests/minute per IP, applied to login/refresh/onboarding.</item>
/// </list>
/// Partitioning by <c>tenant_id</c> keeps one noisy tenant from exhausting another's
/// budget, which is the isolation property that matters for a SaaS API.
/// </summary>
public static class RateLimitingSetup
{
    public const string TenantPolicy = "tenant";
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    TenantPartitionKey(context),
                    _ => Window(options.Tenant)));

            limiter.AddPolicy(TenantPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    TenantPartitionKey(context),
                    _ => Window(options.Tenant)));

            limiter.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"ip:{ClientIp(context)}",
                    _ => Window(options.Auth)));

            limiter.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(RateLimitPolicyOptions policy) => new()
    {
        PermitLimit = policy.PermitLimit,
        Window = TimeSpan.FromSeconds(policy.WindowSeconds),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    };

    private static string TenantPartitionKey(HttpContext context)
    {
        var tenantId = context.User.FindFirst(AuthClaims.TenantId)?.Value;
        return !string.IsNullOrWhiteSpace(tenantId)
            ? $"tenant:{tenantId}"
            : $"ip:{ClientIp(context)}";
    }

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
            Detail = "The rate limit for this tenant or IP address has been exceeded. Retry later.",
            Instance = context.HttpContext.Request.Path,
        };

        problem.Extensions["code"] = "rate_limited";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = context.HttpContext.Items[CorrelationKeys.ItemKey];

        await response.WriteAsJsonAsync(
            problem,
            problem.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}
