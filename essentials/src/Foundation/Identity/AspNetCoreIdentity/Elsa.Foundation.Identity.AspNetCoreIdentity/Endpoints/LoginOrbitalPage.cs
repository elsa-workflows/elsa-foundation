using System.Net;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

internal static class LoginOrbitalPage
{
    public static string Render(LoginPageOptions options, string form, string pathBase)
    {
        static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");

        var logoUrl = LoginPage.SafeUrl(options.LogoUrl) ?? $"{pathBase}/_elsa/identity/login-assets/elsa-logo.png";
        var logo = $"<img class=\"logo\" src=\"{Encode(logoUrl)}\" alt=\"\" />";
        var backdrop = LoginPage.SafeUrl(options.ImageUrl) is { } image
            ? $"<img class=\"custom-backdrop\" src=\"{Encode(image)}\" alt=\"\" />"
            : "";
        var links = string.Join("", new[] { (options.HelpUrl, "Help"), (options.PrivacyUrl, "Privacy") }
            .Where(link => LoginPage.SafeUrl(link.Item1) is not null)
            .Select(link => $"<a href=\"{Encode(LoginPage.SafeUrl(link.Item1))}\">{link.Item2}</a>"));

        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="color-scheme" content="dark" />
          <title>Sign in · {{Encode(options.ProductName)}}</title>
          <style>
            :root { color-scheme:dark; --ink:#f2f7fc; --muted:#a9bacb; --line:rgba(167,207,230,.24); --cyan:#57d9f4; }
            * { box-sizing:border-box; }
            body { margin:0; min-height:100vh; min-height:100svh; overflow-x:hidden; background:#07101b; color:var(--ink); font-family:system-ui,-apple-system,"Segoe UI",sans-serif; }
            .space-scene { position:fixed; z-index:0; inset:0; overflow:hidden; pointer-events:none; background:radial-gradient(ellipse at 50% 4%,rgba(0,115,157,.24),transparent 42%),radial-gradient(ellipse at 12% 54%,rgba(20,66,103,.16),transparent 34%),linear-gradient(150deg,#07101b 12%,#081724 55%,#050b13); }
            .custom-backdrop { position:absolute; inset:0; width:100%; height:100%; object-fit:cover; opacity:.18; mix-blend-mode:screen; }
            .stars { position:absolute; inset:0; opacity:.66; background-image:radial-gradient(1px 1px at 16px 27px,rgba(208,239,255,.9) 50%,transparent 100%),radial-gradient(1px 1px at 83px 96px,rgba(127,203,239,.75) 50%,transparent 100%),radial-gradient(1.5px 1.5px at 143px 48px,rgba(224,246,255,.82) 50%,transparent 100%),radial-gradient(1px 1px at 197px 132px,rgba(140,193,221,.68) 50%,transparent 100%); background-size:223px 181px; mask-image:linear-gradient(to bottom,#000 4%,rgba(0,0,0,.8) 72%,transparent); }
            .grid-glow { position:absolute; inset:0; opacity:.16; background-image:linear-gradient(rgba(115,188,219,.13) 1px,transparent 1px),linear-gradient(90deg,rgba(115,188,219,.13) 1px,transparent 1px); background-size:64px 64px; mask-image:radial-gradient(ellipse at 50% 42%,#000,transparent 76%); }
            .orbit { position:absolute; left:50%; top:48%; width:min(88vw,1120px); aspect-ratio:2.42; border:1px solid rgba(89,201,232,.16); border-radius:50%; transform:translate(-50%,-50%) rotate(-18deg); box-shadow:0 0 44px rgba(20,139,184,.08),inset 0 0 32px rgba(37,131,164,.05); }
            .orbit::before,.orbit::after { content:""; position:absolute; inset:12% -6%; border:1px solid rgba(140,199,223,.11); border-radius:50%; transform:rotate(12deg); }
            .orbit::after { inset:-13% 13%; transform:rotate(-26deg); border-color:rgba(98,180,216,.1); }
            .horizon { position:absolute; left:50%; bottom:-66vw; width:min(128vw,1560px); aspect-ratio:1; transform:translateX(-50%); border-radius:50%; background:radial-gradient(ellipse at 50% 1%,rgba(150,236,255,.96) 0,rgba(46,174,225,.82) .55%,rgba(19,87,144,.84) 2%,rgba(10,36,68,.96) 12%,#06101d 48%); box-shadow:0 -16px 64px rgba(55,197,245,.14),0 -2px 8px rgba(144,235,255,.66); }
            .horizon::after { content:""; position:absolute; inset:2% 5% auto; height:18%; border-top:1px solid rgba(171,233,255,.3); border-radius:50%; filter:blur(1px); }
            main { position:relative; z-index:1; display:grid; place-items:center; min-height:100vh; min-height:100svh; padding:clamp(20px,5vw,72px); }
            .card { position:relative; isolation:isolate; width:min(100%,460px); padding:clamp(28px,5vw,42px); overflow:hidden; border:1px solid rgba(167,213,235,.3); border-radius:24px; background:linear-gradient(145deg,rgba(21,34,48,.88),rgba(12,22,34,.78)); box-shadow:0 32px 100px rgba(0,0,0,.48),0 0 80px rgba(25,132,174,.12),inset 0 1px rgba(245,252,255,.12); -webkit-backdrop-filter:blur(26px) saturate(135%); backdrop-filter:blur(26px) saturate(135%); }
            .card::before { content:""; position:absolute; z-index:-1; inset:0; background:linear-gradient(135deg,rgba(125,215,244,.1),transparent 32%,transparent 74%,rgba(31,130,177,.08)); pointer-events:none; }
            .brand { display:flex; align-items:center; gap:12px; min-width:0; margin-bottom:34px; }
            .logo-frame { display:grid; flex:none; width:48px; height:48px; place-items:center; border:1px solid rgba(106,202,234,.23); border-radius:15px; background:linear-gradient(145deg,rgba(67,158,195,.17),rgba(5,15,27,.56)); box-shadow:inset 0 1px rgba(255,255,255,.12),0 8px 24px rgba(0,0,0,.2); }
            .logo { width:29px; height:29px; object-fit:contain; }
            .brand-copy { display:grid; min-width:0; gap:3px; }
            .brand-kicker { color:#82bbd1; font-size:9px; font-weight:700; letter-spacing:.19em; text-transform:uppercase; }
            .product-name { overflow-wrap:anywhere; color:#e4f0f8; font-size:14px; font-weight:650; letter-spacing:.01em; }
            .eyebrow { display:flex; align-items:center; gap:9px; margin:0 0 12px; color:#8db6ca; font-size:10px; font-weight:650; letter-spacing:.17em; text-transform:uppercase; }
            .eyebrow::before { content:""; width:6px; height:6px; flex:none; border-radius:50%; background:var(--cyan); box-shadow:0 0 11px rgba(87,217,244,.9); }
            h1 { margin:0 0 10px; font-size:clamp(29px,6vw,36px); font-weight:580; letter-spacing:-.045em; line-height:1.12; }
            .subtitle { margin:0 0 9px; color:#c2d0dc; font-size:14px; line-height:1.55; }
            .description { margin:0 0 28px; color:var(--muted); font-size:12px; line-height:1.65; }
            form { margin:0; }
            label { display:block; margin:0 0 8px; color:#d5e0e9; font-size:12px; font-weight:600; }
            input:not([type="hidden"]) { width:100%; min-height:48px; margin:0 0 19px; padding:12px 14px; border:1px solid rgba(156,191,213,.27); border-radius:10px; background:rgba(4,13,23,.54); color:var(--ink); font:inherit; font-size:14px; box-shadow:inset 0 1px 5px rgba(0,0,0,.2); }
            input::placeholder { color:#8599aa; }
            input:focus-visible,button:focus-visible,a:focus-visible { outline:2px solid #78e5ff; outline-offset:3px; }
            button { width:100%; min-height:48px; margin-top:5px; padding:12px 16px; border:1px solid rgba(112,223,248,.47); border-radius:10px; background:linear-gradient(100deg,#0876ae,#078ebd); color:#f4fcff; font:inherit; font-size:14px; font-weight:650; cursor:pointer; box-shadow:0 7px 22px rgba(0,91,139,.27),inset 0 1px rgba(255,255,255,.16); }
            button:hover { filter:brightness(1.12); }
            .login-methods { display:grid; gap:11px; margin:23px 0; }
            .login-methods a { display:block; padding:13px 15px; border:1px solid var(--line); border-radius:10px; color:var(--ink); text-align:center; font-size:13px; text-decoration:none; }
            .login-methods a:hover { background:rgba(118,190,219,.1); }
            .login-methods [data-preferred="true"] { border-color:rgba(87,217,244,.7); box-shadow:0 0 0 1px rgba(87,217,244,.25); }
            .login-methods small { margin-left:7px; color:#a5c5d5; font-size:10px; font-weight:500; }
            .error { margin:0 0 20px; padding:12px 14px; border:1px solid rgba(255,133,126,.44); border-radius:10px; background:rgba(104,35,43,.34); color:#ffaaa4; font-size:13px; line-height:1.5; }
            [role="status"] { color:#c5d7e4; font-size:12px; line-height:1.5; }
            footer { display:flex; flex-wrap:wrap; align-items:center; justify-content:space-between; gap:12px 18px; margin-top:27px; padding-top:18px; border-top:1px solid rgba(165,199,219,.14); color:#8299aa; font-size:10px; }
            .footer-links { display:flex; flex-wrap:wrap; gap:16px; }
            a { color:#a8d7e8; text-underline-offset:3px; }
            @media(max-width:480px) {
              main { padding:18px; }
              .card { border-radius:20px; padding:26px 22px; }
              .brand { margin-bottom:28px; }
              .horizon { bottom:-91vw; width:170vw; }
              .orbit { width:150vw; top:40%; }
            }
            @media(forced-colors:active),(prefers-contrast:more) {
              body { background:Canvas; color:CanvasText; }
              .space-scene { display:none; }
              main { display:flex; justify-content:center; }
              .card { width:100%; max-width:520px; border:1px solid CanvasText; background:Canvas; box-shadow:none; -webkit-backdrop-filter:none; backdrop-filter:none; }
              .brand-kicker,.product-name,.eyebrow,.subtitle,.description,label,footer,.login-methods small,[role="status"] { color:CanvasText; }
              .logo-frame,input:not([type="hidden"]),.login-methods a { border:1px solid CanvasText; background:Canvas; color:CanvasText; }
              button { border:1px solid ButtonText; background:ButtonFace; color:ButtonText; box-shadow:none; }
              .error { border-color:CanvasText; background:Canvas; color:CanvasText; }
              :is(input,button,a):focus-visible { outline-color:Highlight; }
            }
          </style>
        </head>
        <body data-layout="orbital">
          <div class="space-scene" aria-hidden="true">
            {{backdrop}}
            <div class="stars"></div>
            <div class="grid-glow"></div>
            <div class="orbit"></div>
            <div class="horizon"></div>
          </div>
          <main>
            <section class="card" aria-labelledby="signin-heading">
              <div class="brand"><span class="logo-frame">{{logo}}</span><span class="brand-copy"><span class="brand-kicker">Secure identity</span><span class="product-name">{{Encode(options.ProductName)}}</span></span></div>
              <p class="eyebrow">Your workspace awaits</p>
              <h1 id="signin-heading">Welcome back</h1>
              <p class="subtitle">{{Encode(options.Tagline)}}</p>
              <p class="description">{{Encode(options.Description)}}</p>
              {{form}}
              <footer><span>Protected by Elsa Identity</span><span class="footer-links">{{links}}</span></footer>
            </section>
          </main>
        </body>
        </html>
        """;
    }
}