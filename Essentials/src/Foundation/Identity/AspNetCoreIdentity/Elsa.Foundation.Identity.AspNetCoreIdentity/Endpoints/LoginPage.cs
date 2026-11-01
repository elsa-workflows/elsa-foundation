using System.Globalization;
using System.Net;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>The backend-owned credential presentation. Authentication, CSRF and return-url validation remain in the endpoint.</summary>
public static class LoginPage
{
    public static string Render(string returnUrl, bool showError, string? antiforgeryToken = null) =>
        Render(returnUrl, showError, antiforgeryToken, new LoginPageOptions());

    /// <summary>Renders configured presentation or invokes its single host replacement. The return URL must already be sanitized.</summary>
    public static string Render(string returnUrl, bool showError, string? antiforgeryToken, LoginPageOptions options)
        => RenderInShell(returnUrl,showError,antiforgeryToken,options,"");

    internal static string RenderInShell(string returnUrl, bool showError, string? antiforgeryToken, LoginPageOptions options, string pathBase)
    {
        var form = RenderForm(returnUrl, showError, antiforgeryToken, pathBase);
        if (options.Renderer is { } renderer)
            return renderer(new(options.ProductName, returnUrl, showError, antiforgeryToken, form));

        static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
        var layout = options.Layout is "split" or "reverse" or "immersive" or "editorial" ? options.Layout : "split";
        var palette = options.Palette is "linen" or "tidal" or "daybreak" or "canopy" ? options.Palette : "linen";
        var artwork = options.Artwork is "ember" or "tidal" or "daybreak" or "canopy" ? options.Artwork : "ember";
        var image = SafeUrl(options.ImageUrl) ?? $"{pathBase}/_elsa/identity/login-assets/{artwork}.jpg";
        var logoUrl = SafeUrl(options.LogoUrl) ?? $"{pathBase}/_elsa/identity/login-assets/elsa-logo.png";
        var logo = $"<img class=\"logo\" src=\"{Encode(logoUrl)}\" alt=\"\" />";
        var links = string.Join("", new[] { (options.HelpUrl, "Help"), (options.PrivacyUrl, "Privacy") }
            .Where(link => SafeUrl(link.Item1) is not null)
            .Select(link => $"<a href=\"{Encode(SafeUrl(link.Item1))}\">{link.Item2}</a>"));
        static string Coordinate(double value) => (double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50).ToString(CultureInfo.InvariantCulture);
        var focal = $"{Coordinate(options.FocalX)}% {Coordinate(options.FocalY)}%";
        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="color-scheme" content="light dark" />
          <title>Sign in · {{Encode(options.ProductName)}}</title>
          <style>
            :root { --ground:#f3f1ec; --paper:#fffefa; --ink:#34372f; --muted:#65685e; --line:#d9dbd0; --accent:#a64834; --on-accent:#fff; --wash:#eee9e1; color-scheme:light; }
            * { box-sizing:border-box; }
            body { margin:0; min-height:100vh; min-height:100svh; background:var(--ground); color:var(--ink); font-family:system-ui,-apple-system,"Segoe UI",sans-serif; }
            body[data-palette="tidal"] { --ground:#edf2f2; --paper:#fbfdfd; --ink:#243b40; --muted:#50656b; --line:#cbd9da; --accent:#296675; --wash:#e2ecec; }
            body[data-palette="daybreak"] { --ground:#f5eff0; --paper:#fffcfc; --ink:#44373c; --muted:#6f5e66; --line:#dfd1d7; --accent:#925269; --wash:#f0e5e9; }
            body[data-palette="canopy"] { --ground:#eff2eb; --paper:#fcfdf8; --ink:#354333; --muted:#5d6b57; --line:#d0dac8; --accent:#536c40; --wash:#e5ebdf; }
            .entry { display:grid; grid-template-columns:minmax(0,1fr) minmax(400px,.82fr); min-height:100vh; min-height:100svh; padding:20px; gap:20px; }
            .visual { position:relative; min-height:480px; border-radius:26px; overflow:hidden; background:#303930; color:#fff; }
            .visual>img { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; object-position:{{focal}}; }
            .visual::after { content:""; position:absolute; inset:0; background:linear-gradient(180deg,rgba(10,20,16,.04) 25%,rgba(10,20,16,.76)); }
            .story { position:absolute; z-index:1; bottom:0; padding:clamp(28px,4.5vw,72px); max-width:700px; text-shadow:0 1px 5px rgba(0,0,0,.18); }
            .eyebrow { font-size:11px; font-weight:600; letter-spacing:.16em; text-transform:uppercase; }
            .story h2 { font-size:clamp(30px,3.8vw,58px); font-weight:500; line-height:1.12; letter-spacing:-.045em; margin:20px 0; text-wrap:balance; }
            .story p { max-width:370px; font-size:14px; line-height:1.7; margin:0; }
            .welcome { display:flex; flex-direction:column; justify-content:center; padding:clamp(24px,5vw,80px); }
            .brand { display:flex; align-items:center; gap:12px; font-size:14px; font-weight:650; overflow-wrap:anywhere; margin-bottom:64px; }
            .logo { width:34px; height:34px; object-fit:contain; flex-shrink:0; }
            .form-wrap { width:100%; max-width:360px; margin:auto; }
            h1 { margin:0 0 10px; font-size:30px; font-weight:550; letter-spacing:-.035em; }
            .subtitle { margin:0 0 32px; color:var(--muted); font-size:14px; line-height:1.6; }
            label { display:block; font-size:12px; font-weight:600; margin:0 0 8px; }
            input { width:100%; padding:13px 14px; margin:0 0 20px; border:1px solid var(--line); border-radius:11px; background:var(--paper); color:var(--ink); font:inherit; font-size:14px; }
            input:focus-visible,button:focus-visible,a:focus-visible { outline:2px solid var(--accent); outline-offset:3px; }
            button { width:100%; padding:14px; border:1px solid transparent; border-radius:11px; background:var(--accent); color:var(--on-accent); font:inherit; font-size:13px; font-weight:600; cursor:pointer; }
            button:hover { filter:brightness(.94); }
            .error { padding:12px; border:1px solid #c34e44; border-radius:11px; color:#992a24; background:#fff0ed; font-size:13px; line-height:1.5; }
            footer { display:flex; flex-wrap:wrap; gap:18px; margin-top:40px; font-size:11px; color:var(--muted); }
            a { color:inherit; text-underline-offset:3px; }
            [data-layout="reverse"] .visual { order:2; }
            [data-layout="immersive"] .entry { display:flex; justify-content:flex-end; align-items:center; padding:clamp(20px,5vw,80px); }
            [data-layout="immersive"] .visual { position:fixed; inset:0; border-radius:0; }
            [data-layout="immersive"] .story { max-width:55vw; }
            [data-layout="immersive"] .welcome { position:relative; z-index:2; width:440px; border:1px solid var(--line); border-radius:24px; background:var(--paper); padding:40px; box-shadow:0 24px 80px rgba(10,20,16,.15); }
            [data-layout="editorial"] .entry { padding:5vh 6vw; gap:6vw; }
            [data-layout="editorial"] .visual { border-radius:45% 45% 24px 24px; }
            @media(prefers-color-scheme:dark) {
              :root,body[data-palette] { --ground:#20231f; --paper:#292d27; --ink:#f0f1eb; --muted:#bbc1b4; --line:#535a4d; --accent:#e0a28b; --on-accent:#28231f; --wash:#353b30; color-scheme:dark; }
              body[data-palette="tidal"] { --ground:#1d2729; --paper:#253338; --ink:#edf4f5; --muted:#b5c9cc; --line:#536b72; --accent:#acd3dc; --on-accent:#183239; }
              body[data-palette="daybreak"] { --ground:#2b242b; --paper:#362e35; --ink:#f5edf1; --muted:#d2bdc8; --line:#78636e; --accent:#dab2c4; --on-accent:#382630; }
              body[data-palette="canopy"] { --ground:#20271f; --paper:#2b3329; --ink:#eff4e9; --muted:#b6c5a7; --line:#5d6b53; --accent:#b4cd9e; --on-accent:#20301e; }
              .error { color:#ffb6ad; background:#422d2b; }
            }
            @media(max-width:800px) {
              .entry,[data-layout="editorial"] .entry { grid-template-columns:1fr; padding:12px; gap:0; }
              .visual,[data-layout="reverse"] .visual { min-height:260px; order:0; border-radius:20px; }
              .story { padding:28px; }
              .story h2 { font-size:32px; max-width:430px; margin:12px 0; }
              .story p { display:none; }
              .welcome { padding:36px 24px; }
              .brand { margin-bottom:32px; }
              [data-layout="immersive"] .story { display:none; }
              [data-layout="immersive"] .welcome { width:min(440px,100%); padding:32px 24px; }
            }
            @media(forced-colors:active),(prefers-contrast:more) {
              :root,body[data-palette] { --ground:Canvas; --paper:Canvas; --ink:CanvasText; --muted:CanvasText; --line:CanvasText; --accent:Highlight; --on-accent:HighlightText; }
              .visual { display:none; }
              .entry,[data-layout="editorial"] .entry { display:flex; justify-content:center; }
              .welcome { position:relative; width:100%; max-width:520px; box-shadow:none; }
              .error { color:CanvasText; background:Canvas; border-color:CanvasText; }
            }
          </style>
        </head>
        <body data-layout="{{layout}}" data-palette="{{palette}}">
          <main class="entry">
            <section class="visual" aria-label="Welcome">
              <img src="{{Encode(image)}}" alt="" fetchpriority="high" />
              <div class="story"><span class="eyebrow">{{Encode(options.ProductName)}}</span><h2>{{Encode(options.Tagline)}}</h2><p>{{Encode(options.Description)}}</p></div>
            </section>
            <section class="welcome" aria-labelledby="signin-heading">
              <div class="form-wrap">
                <div class="brand">{{logo}}<span>{{Encode(options.ProductName)}}</span></div>
                <h1 id="signin-heading">Welcome back</h1>
                <p class="subtitle">Sign in to your workspace.</p>
                {{form}}
                <footer><span>{{Encode(options.ProductName)}}</span>{{links}}</footer>
              </div>
            </section>
          </main>
        </body>
        </html>
        """;
    }

    private static string RenderForm(string returnUrl, bool showError, string? token, string pathBase)
    {
        var antiforgery = string.IsNullOrEmpty(token) ? "" : $"<input type=\"hidden\" name=\"{AspNetCoreIdentityDefaults.AntiforgeryFieldName}\" value=\"{WebUtility.HtmlEncode(token)}\" />";
        var error = showError ? "<p class=\"error\" role=\"alert\">Invalid username or password.</p>" : "";
        return $$"""
        <form method="post" action="{{WebUtility.HtmlEncode(pathBase)}}/{{AspNetCoreIdentityDefaults.LoginRoute}}">
          {{error}}{{antiforgery}}
          <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl)}}" />
          <label for="username">Username</label>
          <input id="username" name="username" autocomplete="username" autofocus required />
          <label for="password">Password</label>
          <input id="password" name="password" type="password" autocomplete="current-password" required />
          <button type="submit">Sign in</button>
        </form>
        """;
    }

    internal static string? SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') || value.StartsWith("//", StringComparison.Ordinal)) return null;
        if (value.StartsWith('/')) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ? value : null;
    }
}
