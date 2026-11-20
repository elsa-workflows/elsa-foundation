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

WorkspaceStyle accepts `linen`, `minimal`, `quiet`, `focus`, `open`, or `preset` for a theme's original presentation.
PaletteId is an eligible theme ID from the existing [theme store](../src/essentials/Elsa.Studio.Hosting.Themes/README.md).
Mode accepts `system`, `light`, `dark`, `dim`, `high-contrast`; Navigation accepts `theme`, `left`, `top`.
Unsupported modes fall back through the existing palette policy. A host default only fills a missing
user choice. Explicit browser choices win and Reset restores host defaults. Existing profiles that
saved a full theme retain its original presentation; users can select Linen independently.

### Minimal Studio presets

Appearance offers three complete combinations: **Porcelain** (`builtin:porcelain`, Light), **Atelier**
(`builtin:atelier`, Light), and **Graphite** (`builtin:graphite`, Dark). Each selects the Minimal workspace
and its coordinated palette/mode. Navigation remains the user's independent choice. Subsequent
workspace, color and mode changes remain independent; selecting a color swatch alone does not
apply a complete preset. Linen remains the fresh-profile default.

For a branded Atelier default, set `WorkspaceStyle` to `minimal`, `PaletteId` to
`builtin:atelier`, and `Mode` to `light` in the configuration above. Use `builtin:porcelain`/`light` or
`builtin:graphite`/`dark` for the other combinations. Existing browser choices still win. New built-in IDs use a `builtin:` namespace outside the custom kebab-case ID grammar, so existing
custom themes such as `porcelain` or `graphite` remain intact. The earlier
editorial theme keeps its stable ID `atelier` and appears as **Atelier Original**; saved profiles
and integrations using that ID keep its original colors and geometry.

Custom theme JSON can declare `"workspaceStyle": "minimal"` as its default surface treatment.
The Theme Builder exposes this as **Default workspace treatment**. Leaving it absent keeps the
theme's original treatment. Its font, radii and layout remain editable defaults; an explicit
user workspace choice can replace those defaults. Clone, save, directory source, import and
export preserve this metadata. See the [theme store](../src/essentials/Elsa.Studio.Hosting.Themes/README.md).

Minimal uses neutral primary actions, tiny accent/selection marks, dotted secondary dividers and
solid focus indicators. Supply a complete, readable palette for every supported mode rather than
replacing only an accent value. Keep warning, danger and runtime state meanings intact. Logos,
login visuals and replaceable login templates use the independent branding options below.

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
      "Layout": "orbital",
      "HelpUrl": "https://help.example.com",
      "PrivacyUrl": "/privacy"
    }
  }
}
```

An unconfigured identity host uses **Orbital**: a dark deep-space field, fine star and orbital details,
an illuminated planetary horizon, and a centered glass credential panel with Elsa branding. The scene
is decorative and works without external assets or scripts; the real form retains the provider's
username/password fields, CSRF protection and supported sign-in methods. A configured `ImageUrl` adds
a subtle image layer behind the space details.

Set `Layout` to `orbital` to select the new design explicitly. To restore Pocket Orchestra (P5), set
`Layout` to `parcel-room`, and `Palette` and `Artwork` to `pocket-orchestra`. Set all three to `parcel-room`
to use the earlier M2 marker illustration. Existing explicit host choices continue to win. Orbital uses
its own dark palette and CSS scene; layout, palette and artwork remain independent for illustrated layouts.

| Setting | Choices |
|---|---|
| Layout | `orbital` (default; deep-space scene and glass sign-in panel), `parcel-room` (form left, full illustration right), `split` (image left), `reverse` (image right), `immersive` (full image and opaque form card), `editorial` (arched image), `field-guide` (form left, contained illustration right) |
| Palette | `pocket-orchestra` (default for illustrated layouts; warm paper and teal, light only), `parcel-room` (white and teal, light only), `linen` (copper), `tidal` (blue), `daybreak` (rose), `canopy` (green), `field-guide` (paper and sage, light only) |
| Artwork | `pocket-orchestra` (default for illustrated layouts); see the choices below |

The bundled artwork directions are available independently of the selected layout and palette:

| Artwork value | Direction |
|---|---|
| `pocket-orchestra` | Colored-pencil orchestra room with piano, cello, trumpet and a conductor’s score (default) |
| `parcel-room` | Warm marker illustration of sorting envelopes and parcels |
| `river-to-sea` | Aerial waterways opening into a sunlit coastline |
| `open-architecture` | Terracotta archway and a continuing coastal path |
| `flowing-ribbons` | Copper, cream and teal ribbons weaving through space |
| `paper-landscape` | Layered paper hills, river and bridges |
| `creative-studio` | Sunlit workspace with sketches and an unfinished model |
| `botanical-growth` | Branching stems and fresh leaves in filtered sunlight |
| `constellation` | Points of light connected across a midnight sky |
| `miniature-world` | Handcrafted landscape with waterways, bridges and buildings |
| `glass-and-light` | Translucent curved glass and warm refracted light |
| `unfolding-map` | Illustrated landscape with connecting paths and settlements |

The earlier `ember`, `tidal`, `daybreak` and `canopy` artwork values remain supported. Omitted,
empty or unrecognized artwork values use `pocket-orchestra` in illustrated layouts. A valid
`ImageUrl` takes precedence over bundled artwork there; Orbital instead layers the configured image
subtly behind its CSS space scene.

After changing login defaults or renderer code, rebuild and restart the **identity backend host**
(Workbench at `https://localhost:7243` in the default development profile), then reload the sign-in page.
Restarting only Studio does not update this backend-served page. Remove any existing
`Identity:Login:Layout` override or set it to `orbital` if the host still selects an illustrated layout.

Configure the **identity backend host** using appsettings JSON as above, an environment variable such
as `Identity__Login__Artwork=flowing-ribbons`, or a command-line setting
`--Identity:Login:Artwork=flowing-ribbons`. Developers can override configuration after binding:

```csharp
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

builder.Services.PostConfigure<LoginPageOptions>(options =>
{
    options.Layout = "parcel-room";
    options.Artwork = "flowing-ribbons";
    options.ImageUrl = null;
});
```

Artwork ships locally at `/_elsa/identity/login-assets/<artwork>.jpg`; there is no
CDN dependency. Set ImageUrl to your own asset to replace the image in illustrated layouts or add a
subtle background layer to Orbital. FocalX/FocalY are percentages clamped to 0–100 for illustrated
layouts. Use a portrait source for split/editorial and a larger landscape source for immersive;
check the crop at desktop, tablet and 320px. Artwork is decorative (`alt=""`), so essential instructions
belong in real text. A failed image leaves readable copy on a solid surface and a usable form.
Parcel Room contains the complete illustration without cropping or story-copy overlays. On narrow
screens it places the form before the artwork. Its small brand uses the standard Elsa logo unless
`LogoUrl` overrides it; configured Help and Privacy links remain available beneath the form.

Names/copy/URLs are encoded. Asset and resource URLs accept root-relative paths or HTTPS; HTTP loopback
is available for local development. Executable/data/protocol-relative URLs are rejected. Rejected configured Studio resource links are hidden; an omitted setting keeps the default product resource link. Serve SVG only
from a trusted deployment source. Supply your own privacy/help endpoint if you configure a local link.
Orbital stays dark; other palettes follow the OS except for the light-only Field Guide, Parcel Room
and Pocket Orchestra palettes. High contrast removes decorative imagery and effects; the form needs no JavaScript.
External OIDC login pages remain owned and themed by their identity provider.

## Field Guide login theme

The Field Guide presentation matches the Elsa 4 brochure: warm paper, sage/graphite ink,
serif headings, the drawn Elsa logo, and an uncropped pencil/watercolor capability plate.
Configure the **identity backend**, where the credential page is rendered:

```json
{
  "Identity": {
    "Login": {
      "Layout": "field-guide",
      "Palette": "field-guide",
      "Artwork": "field-guide",
      "Tagline": "Workflows, decisions, data, and agents.",
      "Description": "A modular .NET foundation, built in the open."
    }
  }
}
```

The equivalent environment variables are `Identity__Login__Layout=field-guide`,
`Identity__Login__Palette=field-guide`, and `Identity__Login__Artwork=field-guide`.
For code-based composition, configure the same properties on `LoginPageOptions`.
Existing default copy remains configurable through `Tagline`, `Description`, and `ProductName`.

The three choices stay independent. The layout places the form on the left and the complete
illustration on the right; on mobile, the form comes first and the illustration follows it.
The Field Guide palette stays **light**, including when the OS prefers dark appearance.
Other palettes retain their existing OS-driven appearance. High contrast hides decorative
artwork while retaining the form and visible controls. There is no animation or JavaScript
requirement on the credential page.

The layout's default logo is the locally embedded `field-guide-logo.png`; `LogoUrl` still
overrides it. The `field-guide` artwork is served as `field-guide.jpg` through the existing
anonymous login-asset endpoint. Both resources follow the request's shell prefix or proxy
PathBase. `ImageUrl` can replace the illustration, and the Field Guide layout contains the
whole image rather than cropping it. The original brochure art is decorative; instructions
and labels remain real text. A missing image leaves the form usable.

This is an additional login appearance. It does not select a Studio workspace theme, change
existing login defaults, or theme an external identity provider's own login page.

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

For a wholly custom browser shell or additional font/CSS assets, select an application-owned
`StudioHostingOptions.IndexFile` and use the public SDK/UI contracts described in
[custom hosting](custom-hosting.md). The theme store supplies palette definitions; host-owned assets
and presentation do not need a fork of the authentication provider or workflow modules.
