namespace Multitenant.Api.Endpoints;

/// <summary>
/// Minimal browser page used by the confirmation email. The link puts the token in
/// the URL fragment (which browsers never send to the server), and this page reads
/// it client-side and POSTs it to the API so the token does not land in proxy logs,
/// browser history, or Referer headers.
/// </summary>
public static class ConfirmEmailPage
{
    public static IEndpointRouteBuilder MapConfirmEmailPage(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/confirm-email", () => Results.Content(Html, "text/html; charset=utf-8"));
        return endpoints;
    }

    private const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Confirm your email</title>
          <style>
            :root { color-scheme: light dark; }
            body { font-family: system-ui, sans-serif; display: grid; place-items: center; min-height: 100vh; margin: 0; }
            main { max-width: 34rem; padding: 2rem; text-align: center; }
            h1 { font-size: 1.4rem; }
            p { line-height: 1.5; }
          </style>
        </head>
        <body>
          <main>
            <h1>Confirm your email</h1>
            <p id="status">Confirming your address…</p>
          </main>
          <script>
            (async () => {
              const status = document.getElementById('status');
              const params = new URLSearchParams(window.location.hash.slice(1));
              const userId = params.get('userId');
              const token = params.get('token');

              if (!userId || !token) {
                status.textContent = 'This confirmation link is missing its token.';
                return;
              }

              try {
                const response = await fetch('/api/v1/auth/confirm-email', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ userId, token })
                });

                status.textContent = response.ok
                  ? 'Email confirmed. You can close this page and sign in.'
                  : 'This confirmation link is invalid or has expired.';
              } catch {
                status.textContent = 'Could not reach the API. Please try again later.';
              }
            })();
          </script>
        </body>
        </html>
        """;
}
