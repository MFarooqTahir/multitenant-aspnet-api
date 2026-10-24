using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Multitenant.Api.ErrorHandling;
using Multitenant.Application.Common;
using Multitenant.Application.Configuration;
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
/// Authentication endpoints opt into the stricter <see cref="AuthPolicy"/>, which
/// partitions by client IP to slow down credential stuffing before a tenant is known.
/// </para>
/// <para>
/// Options are resolved per request so configuration overrides (tests, environment
/// variables) are honoured without rebuilding the policy.
/// </para>
/// </remarks>
public static class RateLimitingSetup
{
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
