namespace Multitenant.Api.Middleware;

/// <summary>
/// Adds conservative browser-security headers to every response and a focused
/// Content-Security-Policy to the inline confirmation page.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";

        if (context.Request.Path.StartsWithSegments("/confirm-email"))
        {
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; "
                + "frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        }

        await next(context);
    }
}
