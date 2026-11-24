using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Multitenant.Api.ErrorHandling;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration.RateLimiting;

namespace Multitenant.Api.RateLimiting;

/// <summary>
/// Rate limiting for the API.
/// </summary>
/// <remarks>
/// <para>
/// A single global fixed-window limiter partitions by tenant (<c>tenant_id</c> claim),
/// falling back to the client IP for anonymous requests. Partitioning by tenant is
/// what stops one noisy tenant from exhausting another tenant's budget.
/// </para>
/// <para>
/// Credential-entry endpoints (login, 2FA verification, confirmation resend,
/// onboarding) opt into the stricter <see cref="AuthPolicy"/>, which partitions by
/// client IP to slow down credential stuffing before a tenant is known. Routine
/// calls such as refresh and register intentionally stay on the global budget:
/// they are not credential guesses, and a per-IP bucket shared by everyone behind
/// one office NAT would otherwise lock out legitimate sessions. Health probes use
/// a separate per-IP global partition so they neither consume API budgets nor run
/// without a bound.
/// </para>
/// <para>
/// Options are resolved per request so configuration overrides (tests, environment
/// variables) are honoured without rebuilding the policy.
/// </para>
/// </remarks>
public static class RateLimitingSetup
{
    /// <summary>Strict per-IP budget for endpoints that accept credentials.</summary>
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    TenantPartitionKey(context),
                    _ => Window(PolicyOptions(context).Tenant)));

            limiter.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"ip:{ClientIp(context)}",
                    _ => Window(PolicyOptions(context).Auth)));

            limiter.OnRejected = WriteRejectionAsync;
        });

        return services;
    }

    private static RateLimitingOptions PolicyOptions(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

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
        // Probes get their own per-IP bucket: they cannot exhaust a tenant's API
        // budget, and a public readiness endpoint cannot drive unlimited DB checks.
        if (context.Request.Path.Value is "/health" or "/health/live" or "/health/ready")
        {
            return $"health:{ClientIp(context)}";
        }

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
        var httpContext = context.HttpContext;
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        var problem = ApiProblemDetails.Create(
            httpContext,
            StatusCodes.Status429TooManyRequests,
            "Too Many Requests",
            "rate_limited",
            "The rate limit for this tenant or IP address has been exceeded. Retry later.");

        await response.WriteAsJsonAsync(
            problem,
            problem.GetType(),
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);
    }
}
