using Microsoft.AspNetCore.Mvc.Filters;

namespace Multitenant.Api.Filters;

/// <summary>
/// Marks a response as non-cacheable. Applied to the endpoints that return access
/// or refresh tokens so browsers, proxies and shared caches never store them.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoStoreResponseAttribute : ActionFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["Cache-Control"] = "no-store";
        headers["Pragma"] = "no-cache";
    }
}
