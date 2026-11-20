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
        var layout = options.Layout is "split" or "reverse" or "immersive" or "editorial" or "field-guide" or "parcel-room" ? options.Layout : "parcel-room";
        var palette = options.Palette is "pocket-orchestra" or "linen" or "tidal" or "daybreak" or "canopy" or "field-guide" or "parcel-room" ? options.Palette : "pocket-orchestra";
        var lightPalette = palette is "field-guide" or "parcel-room" or "pocket-orchestra";
        var artwork = LoginArtwork.IsKnown(options.Artwork) ? options.Artwork : LoginArtwork.Default;
        var image = SafeUrl(options.ImageUrl) ?? $"{pathBase}/_elsa/identity/login-assets/{artwork}.jpg";
        var logoName = layout == "field-guide" ? "field-guide-logo.png" : "elsa-logo.png";
        var logoUrl = SafeUrl(options.LogoUrl) ?? $"{pathBase}/_elsa/identity/login-assets/{logoName}";
        var logo = $"<img class=\"logo\" src=\"{Encode(logoUrl)}\" alt=\"\" />";
        var links = string.Join("", new[] { (options.HelpUrl, "Help"), (options.PrivacyUrl, "Privacy") }
            .Where(link => SafeUrl(link.Item1) is not null)
            .Select(link => $"<a href=\"{Encode(SafeUrl(link.Item1))}\">{link.Item2}</a>"));
        static string Coordinate(double value) => (double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50).ToString(CultureInfo.InvariantCulture);
        var focal = $"{Coordinate(options.FocalX)}% {Coordinate(options.FocalY)}%";
        return $$"""
        <!doctype html>
        <html lang="en"{{(lightPalette ? " data-appearance=\"light\"" : "")}}>
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="color-scheme" content="{{(lightPalette ? "light" : "light dark")}}" />
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
            /* Parcel Room: a small brand, clear form and full marker illustration on white. */
            body[data-palette="parcel-room"] { --ground:#fff; --paper:#fff; --ink:#18212b; --muted:#626d79; --line:#8a939f; --accent:#58a4b5; --on-accent:#10262d; --wash:#f1f6f7; color-scheme:light; }
            body[data-palette="pocket-orchestra"] { --ground:#faf8f5; --paper:#faf8f5; --ink:#2d3030; --muted:#686d6b; --line:#858c89; --accent:#8bbfc0; --on-accent:#183234; --wash:#eeefea; color-scheme:light; }
            body[data-palette="pocket-orchestra"] :is(input,button,a):focus-visible { outline-color:var(--ink); }
            [data-layout="parcel-room"] .entry { max-width:1600px; margin:auto; grid-template-columns:minmax(300px,.85fr) minmax(0,1.45fr); align-items:center; gap:clamp(32px,5vw,88px); padding:40px 5vw; }
            [data-layout="parcel-room"] .welcome { order:0; min-width:0; padding:32px 0; }
            [data-layout="parcel-room"] .form-wrap { max-width:380px; }
            [data-layout="parcel-room"] .brand { gap:10px; font-size:18px; margin-bottom:44px; }
            [data-layout="parcel-room"] .logo { width:32px; height:32px; }
            [data-layout="parcel-room"] h1 { font-size:clamp(28px,3vw,40px); font-weight:650; line-height:1.15; }
            [data-layout="parcel-room"] .subtitle { font-size:15px; margin-bottom:32px; overflow-wrap:anywhere; }
            [data-layout="parcel-room"] label { font-size:14px; }
            [data-layout="parcel-room"] input { padding:14px; font-size:16px; border-radius:9px; margin-bottom:24px; }
            [data-layout="parcel-room"] button { min-height:48px; margin-top:8px; border-radius:9px; font-size:15px; }
            [data-layout="parcel-room"] :is(input,button,a):focus-visible { outline-color:var(--ink); }
            [data-layout="parcel-room"] .visual { order:1; min-height:0; border-radius:0; overflow:visible; background:transparent; color:var(--ink); }
            [data-layout="parcel-room"] .visual>img { position:static; display:block; width:100%; height:auto; aspect-ratio:1; object-fit:contain; }
            [data-layout="parcel-room"] .visual::after,[data-layout="parcel-room"] .story { display:none; }
            [data-layout="parcel-room"] footer { margin-top:30px; font-size:12px; }
            /* Field Guide: the brochure's paper, sage ink, serif type and uncropped pencil plate. */
            body[data-palette="field-guide"] { --ground:#f5f2ea; --paper:#fdfbf5; --ink:#293c34; --muted:#626d63; --line:#d8dcd0; --accent:#293c34; --on-accent:#fdfbf5; --wash:#e6e9dd; color-scheme:light; }
            [data-layout="field-guide"] .entry { max-width:1500px; margin:auto; grid-template-columns:minmax(300px,.82fr) minmax(0,1.18fr); align-items:center; gap:5vw; padding:48px 6vw; }
            [data-layout="field-guide"] .welcome { order:0; padding:30px 0; }
            [data-layout="field-guide"] .form-wrap { max-width:350px; }
            [data-layout="field-guide"] .brand { gap:12px; font-size:17px; font-weight:500; margin-bottom:58px; }
            [data-layout="field-guide"] .logo { width:46px; height:46px; mix-blend-mode:multiply; }
            [data-layout="field-guide"] h1 { font:400 clamp(36px,3.8vw,52px)/1.12 Georgia,"Times New Roman",serif; letter-spacing:-.045em; margin-bottom:16px; }
            [data-layout="field-guide"] .subtitle { font-size:13px; line-height:1.8; margin-bottom:30px; }
            [data-layout="field-guide"] label { font-size:12px; font-weight:500; }
            [data-layout="field-guide"] input { border-radius:4px; padding:13px 14px; font-size:14px; }
            [data-layout="field-guide"] button { border-radius:28px; font-size:12px; font-weight:500; padding:14px 18px; }
            [data-layout="field-guide"] .error { border-radius:4px; }
            [data-layout="field-guide"] footer { border-top:1px solid var(--line); margin-top:34px; padding-top:20px; font-size:10px; }
            [data-layout="field-guide"] .visual { order:1; min-height:0; display:flex; flex-direction:column; align-items:center; border-radius:0; overflow:visible; background:transparent; color:var(--ink); }
            [data-layout="field-guide"] .visual>img { position:static; width:100%; height:auto; aspect-ratio:3/2; object-fit:contain; mix-blend-mode:multiply; mask-image:linear-gradient(to right,transparent,#000 3%,#000 97%,transparent),linear-gradient(to bottom,transparent,#000 3%,#000 97%,transparent); mask-composite:intersect; }
            [data-layout="field-guide"] .visual::after { display:none; }
            [data-layout="field-guide"] .story { position:static; max-width:480px; padding:26px 14px 0; text-align:center; text-shadow:none; }
            [data-layout="field-guide"] .story .eyebrow { display:none; }
            [data-layout="field-guide"] .story h2 { font:italic 400 clamp(23px,2.5vw,32px)/1.35 Georgia,"Times New Roman",serif; letter-spacing:-.025em; margin:0 0 12px; }
            [data-layout="field-guide"] .story p { color:var(--muted); font-size:12px; line-height:1.8; max-width:none; }
            @media(max-width:1000px) and (min-width:801px) {
              [data-layout="field-guide"] .entry { gap:3vw; padding:32px 5vw; }
              [data-layout="field-guide"] .story { padding:18px 0 0; }
            }
            @media(max-width:800px) {
              [data-layout="field-guide"] .entry { grid-template-columns:minmax(0,1fr); gap:36px; padding:28px 7vw 38px; }
              [data-layout="field-guide"] .welcome { padding:0; }
              [data-layout="field-guide"] .form-wrap { max-width:420px; }
              [data-layout="field-guide"] .brand { margin-bottom:32px; font-size:16px; }
              [data-layout="field-guide"] .logo { width:40px; height:40px; }
              [data-layout="field-guide"] h1 { font-size:38px; margin-bottom:12px; }
              [data-layout="field-guide"] .subtitle { margin-bottom:24px; }
              [data-layout="field-guide"] .visual { order:1; min-height:0; border-radius:0; width:100%; max-width:580px; margin:auto; }
              [data-layout="field-guide"] .story { padding-top:18px; }
              [data-layout="field-guide"] .story h2 { font-size:24px; }
              [data-layout="field-guide"] .story p { display:block; }
              [data-layout="field-guide"] footer { margin-top:26px; padding-top:18px; }
            }
            @media(prefers-color-scheme:dark) {
              :root:where(:not([data-appearance="light"])),body[data-palette]:where(:not([data-palette="field-guide"],[data-palette="parcel-room"],[data-palette="pocket-orchestra"])) { --ground:#20231f; --paper:#292d27; --ink:#f0f1eb; --muted:#bbc1b4; --line:#535a4d; --accent:#e0a28b; --on-accent:#28231f; --wash:#353b30; color-scheme:dark; }
              body[data-palette="tidal"] { --ground:#1d2729; --paper:#253338; --ink:#edf4f5; --muted:#b5c9cc; --line:#536b72; --accent:#acd3dc; --on-accent:#183239; }
              body[data-palette="daybreak"] { --ground:#2b242b; --paper:#362e35; --ink:#f5edf1; --muted:#d2bdc8; --line:#78636e; --accent:#dab2c4; --on-accent:#382630; }
              body[data-palette="canopy"] { --ground:#20271f; --paper:#2b3329; --ink:#eff4e9; --muted:#b6c5a7; --line:#5d6b53; --accent:#b4cd9e; --on-accent:#20301e; }
              :where(body:not([data-palette="field-guide"],[data-palette="parcel-room"],[data-palette="pocket-orchestra"])) .error { color:#ffb6ad; background:#422d2b; }
              [data-layout="field-guide"]:not([data-palette="field-guide"]) .visual>img { mix-blend-mode:normal; }
              [data-layout="field-guide"]:not([data-palette="field-guide"]) .logo { mix-blend-mode:normal; background:#f5f2ea; border-radius:4px; }
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
              [data-layout="parcel-room"] .entry { grid-template-columns:minmax(0,1fr); align-items:start; gap:28px; padding:28px 24px 40px; }
              [data-layout="parcel-room"] .welcome { padding:0; }
              [data-layout="parcel-room"] .form-wrap { max-width:420px; }
              [data-layout="parcel-room"] .brand { margin-bottom:32px; }
              [data-layout="parcel-room"] h1 { font-size:30px; }
              [data-layout="parcel-room"] .visual { width:100%; max-width:520px; margin:auto; }
            }
            @media(forced-colors:active),(prefers-contrast:more) {
              :root,body[data-palette] { --ground:Canvas; --paper:Canvas; --ink:CanvasText; --muted:CanvasText; --line:CanvasText; --accent:Highlight; --on-accent:HighlightText; }
              .visual,[data-layout="field-guide"] .visual { display:none; }
              .entry,[data-layout="editorial"] .entry,[data-layout="field-guide"] .entry,[data-layout="parcel-room"] .entry { display:flex; justify-content:center; }
              .welcome { position:relative; width:100%; max-width:520px; box-shadow:none; }
              .error { color:CanvasText; background:Canvas; border-color:CanvasText; }
              [data-layout="field-guide"] button,[data-layout="parcel-room"] button { background:ButtonFace; color:ButtonText; border-color:ButtonText; }
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
                <p class="subtitle">{{(layout == "parcel-room" ? $"Sign in to {Encode(options.ProductName)}." : "Sign in to your workspace.")}}</p>
                {{form}}
                {{(layout == "parcel-room" && links.Length == 0 ? "" : $"<footer><span>{Encode(options.ProductName)}</span>{links}</footer>")}}
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
