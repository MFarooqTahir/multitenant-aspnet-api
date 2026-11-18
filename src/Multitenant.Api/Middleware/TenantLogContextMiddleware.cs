using Multitenant.Application.Common;
using Serilog.Context;

namespace Multitenant.Api.Middleware;

/// <summary>
/// Adds the resolved tenant and user ids to the Serilog context after
/// authentication, so a single log query can isolate one tenant's requests.
/// Anonymous requests (login, refresh, onboarding) have no claims and are left
/// unchanged.
/// </summary>
public sealed class TenantLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.User.FindFirst(AuthClaims.TenantId)?.Value;
        var userId = context.User.FindFirst(AuthClaims.Subject)?.Value;

        using var tenantProperty = tenantId is null
            ? null
            : LogContext.PushProperty("TenantId", tenantId);

        using var userProperty = userId is null
            ? null
            : LogContext.PushProperty("UserId", userId);

        await next(context);
    }
}
