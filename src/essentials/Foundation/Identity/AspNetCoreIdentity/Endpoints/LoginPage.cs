using System.Net;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>
/// <c>GET /_elsa/identity/login</c> — the out-of-the-box, backend-served login page. This is a locked
/// product decision: the Studio SPA never renders login UI; instead this minimal, self-contained (no CDN
/// assets) dark-themed page posts to the login endpoint and redirects to a validated local
/// <c>returnUrl</c>. Non-local return URLs are dropped to prevent open redirects. The page embeds an
/// antiforgery token (and sets the paired cookie) that the login POST validates for the HTML-form flow.
/// </summary>
public static class LoginPage
{
    /// <summary>
    /// Renders the self-contained dark-themed login page. <paramref name="returnUrl"/> is expected to be
    /// pre-sanitized (see <see cref="LocalUrl.Sanitize"/>) and is HTML-encoded before embedding.
    /// <paramref name="antiforgeryToken"/> is embedded as a hidden field so the POST can validate CSRF.
    /// Exposed for unit testing the markup and the return-URL handling.
    /// </summary>
    public static string Render(string returnUrl, bool showError, string? antiforgeryToken = null)
    {
        var encodedReturnUrl = WebUtility.HtmlEncode(returnUrl);
        var antiforgeryField = string.IsNullOrEmpty(antiforgeryToken)
            ? string.Empty
            : $"""<input type="hidden" name="{AspNetCoreIdentityDefaults.AntiforgeryFieldName}" value="{WebUtility.HtmlEncode(antiforgeryToken)}" />""";
        var errorBanner = showError
            ? """<p class="error" role="alert">Invalid username or password.</p>"""
            : string.Empty;

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Sign in &middot; Elsa</title>
          <style>
            :root { color-scheme: dark; }
            * { box-sizing: border-box; }
            body {
              margin: 0; min-height: 100vh;
              display: flex; align-items: center; justify-content: center;
              background: #0b1120;
              color: #e2e8f0;
              font-family: system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            }
            .card {
              width: 100%; max-width: 360px;
              background: #111a2e;
              border: 1px solid #1e293b;
              border-radius: 12px;
              padding: 32px;
              box-shadow: 0 10px 40px rgba(0,0,0,.4);
            }
            .brand { font-size: 20px; font-weight: 600; margin: 0 0 4px; color: #f8fafc; }
            .subtitle { margin: 0 0 24px; font-size: 13px; color: #94a3b8; }
            label { display: block; font-size: 13px; margin: 0 0 6px; color: #cbd5e1; }
            input {
              width: 100%; padding: 10px 12px; margin-bottom: 16px;
              background: #0b1120; color: #e2e8f0;
              border: 1px solid #334155; border-radius: 8px; font-size: 14px;
            }
            input:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,.25); }
            button {
              width: 100%; padding: 11px; border: 0; border-radius: 8px;
              background: #3b82f6; color: #fff; font-size: 14px; font-weight: 600; cursor: pointer;
            }
            button:hover { background: #2563eb; }
            .error {
              margin: 0 0 16px; padding: 10px 12px; font-size: 13px;
              background: rgba(239,68,68,.12); border: 1px solid rgba(239,68,68,.4);
              border-radius: 8px; color: #fca5a5;
            }
          </style>
        </head>
        <body>
          <form class="card" method="post" action="/{{AspNetCoreIdentityDefaults.LoginRoute}}">
            <h1 class="brand">Elsa</h1>
            <p class="subtitle">Sign in to continue</p>
            {{errorBanner}}
            {{antiforgeryField}}
            <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}" />
            <label for="username">Username</label>
            <input id="username" name="username" autocomplete="username" autofocus required />
            <label for="password">Password</label>
            <input id="password" name="password" type="password" autocomplete="current-password" required />
            <button type="submit">Sign in</button>
          </form>
        </body>
        </html>
        """;
    }
}
