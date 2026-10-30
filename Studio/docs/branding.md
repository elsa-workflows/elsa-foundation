# Brand Studio and sign-in

Linen is the default Studio presentation: warm surfaces, contrasting navigation, rounded geometry and
sans-serif type. Presentation, color scheme, appearance mode and navigation are separate choices.
Existing palettes and workspace arrangements remain available, including original full presets.

## Configure the Studio host

The standard browser reads an explicit public allow-list from `/studio-runtime.js`:

```json
{
  "Studio": {
    "Branding": {
      "ProductName": "Northstar Studio",
      "Subtitle": "Automation workspace",
      "LogoUrl": "/branding/logo.svg",
      "FaviconUrl": "/branding/favicon.svg",
      "HelpUrl": "https://help.example.com",
      "PrivacyUrl": "/privacy"
    },
    "Appearance": {
      "WorkspaceStyle": "linen",
      "PaletteId": "linen",
      "Mode": "system",
      "Navigation": "left"
    }
  }
}
```

Serve application assets through the host's static-file provider. A logo is fitted inside the existing
mark area; supply generous internal spacing and check it against dark navigation. A failed custom
logo falls back to the bundled Elsa mark. Empty Subtitle hides the secondary brand text. ProductName
also sets the browser title. Help and Privacy replace the two header resource links; set either URL to an empty string to hide that link. Names are plain
text; HTML is never interpreted. Only named public fields are serialized: keep credentials elsewhere.

WorkspaceStyle accepts `linen`, `quiet`, `focus`, `open`, or `preset` for a theme's original presentation.
PaletteId is an eligible theme ID from the existing [theme store](../src/essentials/Elsa.Studio.Hosting.Themes/README.md).
Mode accepts `system`, `light`, `dark`, `dim`, `high-contrast`; Navigation accepts `theme`, `left`, `top`.
Unsupported modes fall back through the existing palette policy. A host default only fills a missing
user choice. Explicit browser choices win and Reset restores host defaults. Existing profiles that
saved a full theme retain its original presentation; users can select Linen independently.

## Configure the actual credential page

The password page belongs to the identity backend, while Studio shows the redirect/sign-in status.
Configure both hosts when they are separate deployments. The built-in ASP.NET Core Identity provider
binds `Identity:Login` without a dependency on Studio:

```json
{
  "Identity": {
    "Login": {
      "ProductName": "Northstar Studio",
      "LogoUrl": "/branding/logo.svg",
      "Tagline": "Make room for possibility.",
      "Description": "Thoughtful automation for the work that matters.",
      "Layout": "split",
      "Palette": "linen",
      "Artwork": "ember",
      "FocalX": 50,
      "FocalY": 45,
      "HelpUrl": "https://help.example.com",
      "PrivacyUrl": "/privacy"
    }
  }
}
```

Layout, palette and artwork are independent:

| Setting | Choices |
|---|---|
| Layout | `split` (image left), `reverse` (image right), `immersive` (full image and opaque form card), `editorial` (arched image) |
| Palette | `linen` (copper), `tidal` (blue), `daybreak` (rose), `canopy` (green) |
| Artwork | `ember`, `tidal`, `daybreak`, `canopy` |

The four generated photographs ship locally at `/_elsa/identity/login-assets/<artwork>.jpg`; there is no
CDN dependency. Set ImageUrl to your own asset to replace the image. FocalX/FocalY are percentages
clamped to 0–100. Use a portrait source for split/editorial and a larger landscape source for immersive;
check the crop at desktop, tablet and 320px. Artwork is decorative (`alt=""`), so essential instructions
belong in real text. A failed image leaves readable copy on a solid surface and a usable form.

Names/copy/URLs are encoded. Asset and resource URLs accept root-relative paths or HTTPS; HTTP loopback
is available for local development. Executable/data/protocol-relative URLs are rejected. Serve SVG only
from a trusted deployment source. Supply your own privacy/help endpoint if you configure a local link.
Dark appearance follows the OS and high contrast removes the artwork; the form needs no JavaScript.
External OIDC login pages remain owned and themed by their identity provider.

## Replace login presentation

Use the single `LoginPageOptions.Renderer` callback in the identity host. It replaces the document,
without replacing credential validation, antiforgery issuance or the trusted return-url policy:

```csharp
using System.Net;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

builder.Services.PostConfigure<LoginPageOptions>(options =>
{
    options.Renderer = context => $$"""
        <!doctype html>
        <html lang="en">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Sign in · {{WebUtility.HtmlEncode(context.ProductName)}}</title>
        <link rel="stylesheet" href="/branding/sign-in.css">
        <main aria-labelledby="title">
          <h1 id="title">Welcome back</h1>
          {{context.FormHtml}}
        </main>
        </html>
        """;
});
```

The context includes ProductName, sanitized ReturnUrl, ShowError, AntiforgeryToken and trusted FormHtml.
Keeping FormHtml preserves field names, action, error feedback, autocomplete and the hidden CSRF/return
fields. If you author a new form, retain those semantics and encode values. The callback returns a full
HTML document; trusted host code owns its encoding, assets and accessibility. It is one replacement,
not a contribution list. Apply PostConfigure once so configuration binding has already completed.

## Brand modules

Use [shared design tokens](design-tokens.md) and the shared UI package. Modules own their layout and
consume semantic roles for panel, canvas, text, edges, accent and navigation. Do not key module styling
on a palette ID or copy palette literals. Keep background recipes separate from color roles, and
preserve semantic runtime/error/selection states. The same module then works under Linen, other
arrangements, custom palettes and all supported appearance modes.

Check readable contrast, visible keyboard focus, long names, narrow layouts, missing assets, browser
zoom, OS contrast and reduced motion. Keep form surfaces opaque over photographs and never place
essential low-contrast text directly over an uncontrolled image.

A complete configuration and launch example is in [branded-host](../examples/branded-host/README.md).
Host composition and deployment remain documented in the [hosting guide](custom-hosting.md).
