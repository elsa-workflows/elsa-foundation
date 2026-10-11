# Studio design tokens — the module-author contract

This is the versioned token API that independently-bundled Studio modules style against. A
module that consumes only the tokens documented here renders correctly under every Studio
theme, light/dark mode, and material treatment — present and future — because re-theming
happens entirely in the token layer the host owns, never in module CSS.

The contract is enforced. Module CSS is linted by `elsa/no-raw-color-literal`
(`stylelint/no-raw-color-literal.mjs`, run via `pnpm lint:css`), which bans raw colour
literals in colour-painting declarations. See [Rules](#rules) and [Enforcement](#enforcement).

## The two layers

Colours flow through two layers. A module should reference **layer 2** (or a module-private
alias onto it), never layer 1 directly.

| Layer | Vocabulary | Who owns it | Where |
| --- | --- | --- | --- |
| **1 — primitives** | `--background`, `--primary`, `--radius`, `--ring`, … | the **host** supplies these; `ThemeProvider` rewrites them per theme/mode at runtime | host `app/styles.css` `:root`; a standalone consumer supplies them itself (see `examples/*/src/theme.css`) |
| **2 — semantic** | `--studio-*` | the shared token layer; each is an alias onto a layer-1 primitive | `src/apps/Elsa.Studio.Web/Client/src/app/ui/tokens.css` |

Layer 2 is almost entirely aliases (`--studio-bg: var(--background)`), so remapping layer 1
re-themes every `--studio-*` token at once. The published `@elsa-workflows/studio-ui/tokens.css`
ships layer 2 plus a light-theme layer-1 snapshot so it resolves standalone
(`src/apps/Elsa.Studio.Web/StudioUi/scripts/build-tokens.mjs`).

### What a host must supply (layer 1)

A host that mounts Studio widgets outside `Elsa.Studio.Web` supplies the layer-1 primitives.
The canonical minimal set is the two example hosts' `theme.css` — treat that file as the
layer-1 reference:

```
--font-sans  --font-mono
--radius-sm  --radius  --radius-md  --radius-lg
--spacing-xs --spacing-sm --spacing-md --spacing-lg
--shadow-sm
--primary --primary-foreground
--success --success-foreground
--warning --warning-foreground
--danger  --danger-foreground
--background --foreground --card --muted --muted-foreground --border --ring
```

Override these (or the `--studio-*` aliases directly) in a stylesheet loaded **after**
`tokens.css` to re-theme. For dark mode set `data-theme-mode="dark"` on `<html>` and supply
dark layer-1 values; the alias layer flips the tinted-chip foreground tokens for you.
Optionally supply `--font-display` (title face; defaults to `--font-sans`).

### Modes and the `<html>` attributes

Studio offers four modes: `light`, `dark`, `dim` and `high-contrast`. Every theme defines Light
and Dark; Dim and High contrast are optional per theme (the seven signature themes — Meridian,
Drift, Schematic, Atelier, Elsa Cloud, Signal, Dusk — define all four). ThemeProvider sets two attributes:

| Attribute | Values | Meaning |
| --- | --- | --- |
| `data-theme-mode` | `light` \| `dark` | the **colour scheme**. Dim and High contrast are `dark`, so existing `[data-theme-mode="dark"]` rules keep applying |
| `data-theme-appearance` | `light` \| `dark` \| `dim` \| `high-contrast` | the exact mode the user picked |

A third attribute carries the theme's **layout** — `data-theme-layout`, one of `classic`
(default), `floating` (Drift), `workbench` (Schematic) or `editorial` (Atelier). Layout is the one
theme property that changes arrangement rather than paint:

| Layout | Shell | Graph-authoring workbench |
| --- | --- | --- |
| `classic` | docked sidebar, topbar, bottom panel | docked palette / canvas / inspector |
| `floating` | shell regions become rounded islands | palette and inspector float over a full-bleed canvas; curved connections |
| `workbench` | dense topbar, mono dock tabs; navigation defaults to `top` (see below) | dense palette, drafting-grid canvas, squared connections |
| `editorial` | page as a raised sheet; serif italic section headings | no activity toolbox — an **Add step** button and an always-visible connection "+" add steps inline; curved connections |

A module adapts to layout the same way it adapts to mode: CSS guarded on
`html[data-theme-layout="…"]`, styled only through tokens, for arrangement; and, only where the layout
changes markup or behaviour (an affordance that exists in one layout and not another),
`useStudioThemeLayout()` from `@elsa-workflows/studio-sdk`. Keep layout CSS for a surface in a
stylesheet that surface imports, so it does not grow shared landing-path CSS.

A fourth attribute, `data-nav-mode` (`left` | `top`), says where the main navigation sits, and is
a separate axis from layout ([ADR 0022](adr/0022-let-the-user-choose-where-the-navigation-sits.md)).
The user picks **Theme default**, **Left** or **Top** in the theme menu (kept in `localStorage` as
`elsa-studio-nav-mode`); Theme default resolves from the layout — `workbench` → `top`, every other
layout → `left`.

| Navigation mode | Shell |
| --- | --- |
| `left` | docked sidebar with section headings, nested child links, backend status in its footer, and the icon-rail collapse |
| `top` | horizontal menubar: child links become dropdowns, the module search moves right, backend status moves to a status bar |

It is host chrome. The shell's menubar and status-bar rules key on `html[data-nav-mode="top"]` and
never on a layout or theme, and the mode applies at desktop widths only (below 1025px both share
the responsive strip). Modules should not need it: a module that adapts to arrangement keys on
`data-theme-layout`, which the navigation choice never changes.

Modules should need neither: the tokens already resolve per mode, and High contrast additionally
zeroes shadows, makes `--studio-focus` the solid ring colour and brightens the tinted-chip `-fg`
tokens. If a module genuinely needs a High-contrast-only adjustment (e.g. thickening a hairline it
draws with a literal width), it may guard on `html[data-theme-appearance="high-contrast"]`.

## Layer 2 — the `--studio-*` semantic tokens (STABLE)

These are the primary module-facing surface. Stable: renamed or removed only with a major
version bump and a migration note.

### Surfaces & text

| Token | Meaning |
| --- | --- |
| `--studio-bg` | app/page background |
| `--studio-surface` | default raised surface (cards, panels) |
| `--studio-surface-muted` | recessed/secondary surface |
| `--studio-surface-raised` | the most-raised surface tier |
| `--studio-text` | primary foreground |
| `--studio-text-muted` | secondary/label foreground |
| `--studio-border` | hairline border/divider |

### Accent & focus

| Token | Meaning |
| --- | --- |
| `--studio-accent` | selection/primary accent fill |
| `--studio-accent-text` | foreground ON the accent fill |
| `--studio-accent-soft` | low-opacity accent wash (hover/selected rows) |
| `--studio-focus` | focus-ring colour |

### Semantic status

Each status has a saturated **fill** (`--studio-{tone}`, correct as a solid background or
border) and a WCAG-AA **foreground** (`--studio-{tone}-fg`, for tinted-chip text — the fill
fails AA as small text on its own 10–13% tint). Use `-fg` for text on a tinted chip; use the
fill for solid backgrounds/borders. `-text` is the foreground ON a solid fill.

`--studio-success` / `--studio-success-fg` / `--studio-success-text`, and likewise
`--studio-warning*`, `--studio-danger*`.

### Type & radius

`--studio-font-ui`, `--studio-font-mono`, `--studio-font-display` (page, section and dialog
titles), `--studio-title-weight` (optional; a theme whose display face has a single weight sets it
so titles don't render in faux bold — read it as `var(--studio-title-weight, 700)`);
`--studio-page-title-size`, `--studio-section-title-size`, `--studio-body-size`,
`--studio-label-size`;
`--studio-radius-sm`, `--studio-radius`, `--studio-radius-md`, `--studio-radius-lg`,
`--studio-shadow-sm`.

### Compatibility aliases (STABLE, thin)

For modules bundled before the `--studio-*` layer existed: `--surface`, `--text`, `--muted`,
`--accent`. Prefer the `--studio-*` names in new code; these remain as a compatibility shim.

## Layer 2 material vocabulary — `--studio-material-*` (STABLE, opt-in)

A material theme adds textured, layered surface recipes on top of the flat `--studio-*` layer.
They are gated on the `html[data-theme-material]` attribute, so **flat themes are untouched** (see
[Rules](#rules)). A module opts into the material look by consuming these tokens **inside a
`[data-theme-material]` guard**; without the guard the module stays flat.

No built-in theme is a material theme any more (Stone, Blueprint and Brass Instrument were the
last; see [Removed in the next major version](#removed-in-the-next-major-version)), so Studio's own
shell never sets `data-theme-material`. The vocabulary stays for hosts and themes that supply their
own recipes: the shared `html[data-theme-material]` block in `tokens.css` defines a neutral, flat
fallback for every token below, and a material theme overrides the rungs it restyles. If you add a
new `--studio-material-*` token you MUST give it a fallback in that shared block.

### The surface-role ladder

Material surfaces form a role ladder, recessed → raised. Pick the rung by role, not by look:

```
shell  <  panel  <  card  <  row  <  well
```

- **shell / body** — the page backdrop: `--studio-material-body-bg` (+ `-size`),
  `--studio-material-content-bg`, `--studio-material-canvas-bg`.
- **panel** — a framed container slab: `--studio-material-panel-bg` and its
  `-bg-strong` (most raised) / `-bg-soft` (least raised) variants, each with a `-bg-size`.
- **card / node** — a discrete raised object: `--studio-material-node-bg` (+ `-size`),
  `--studio-material-node-shadow`.
- **row** *(dense-list tier)* — `--studio-material-row-bg` (+ `-bg-size`). The **flat** fill
  for dense resource rows: grain/texture over the base tone with the panel's top bevel
  highlight and drop shadow **removed**, so it tiles cleanly at row height. A dense list is
  ONE container slab with flat hairline rows — never a raised `panel-bg-strong` slab per row
  (that tiles the bevel into a ridged "corduroy" texture). Rows separate with a hairline
  border; hover uses `--studio-accent-soft`.
- **well** *(recessed input tier)* — `--studio-material-well-bg` (+ `-bg-size`) and
  `--studio-material-well-shadow`. The sunken tier for search/filter text inputs, which should
  read as recessed wells (inset shadow, darker fill), not raised panels.

### Shared material recipe tokens

Edges/shadows/interaction: `--studio-material-edge`, `--studio-material-edge-strong`,
`--studio-material-shadow`, `--studio-material-shadow-strong`, `--studio-material-inset`,
`--studio-material-inset-low`, `--studio-material-hover-bg`, `--studio-material-active-bg`,
`--studio-material-control-bg`, `--studio-material-send-bg`, `--studio-material-send-text`.
Texture/grid: `--studio-material-texture`, `--studio-material-texture-size`,
`--studio-material-grid-size`, `--studio-material-radius`.

## Rules

1. **No raw colour literals in module CSS.** Never write a hex / `rgb()` / `rgba()` / `hsl()`
   / `oklch()` / … as the value of `color`, `background`, `border*`, `box-shadow`, `outline`,
   `fill`, or `stroke`. Resolve through a token. Allowed compositions: `var(--token)`,
   `var(--token, <fallback>)` (fallback literal is a last-resort default), `color-mix(…)` over
   tokens, gradients whose stops are tokens/`color-mix`, and the keywords `transparent`,
   `currentColor`, `inherit`, `unset`, `initial`, `none`. Enforced by
   `elsa/no-raw-color-literal`.
2. **Never reference another module's private namespace.** A module's `--wf-*`, `--otel-*`,
   `--secrets-*`, … aliases are private to that module. Depend on `--studio-*` (or your own
   private alias onto it), never on `--wf-panel` etc.
3. **No `[data-theme="…"]` selectors in module CSS.** Modules are theme-agnostic: they get
   per-theme behaviour through the tokens, which the host redefines per theme. There are no
   exceptions.
   This rule is about `data-theme` (which theme). The mode and layout attributes are not theme
   selectors and stay allowed: `[data-theme-mode="dark"]` for the colour scheme,
   `[data-theme-appearance="high-contrast"]` for a High-contrast-only adjustment the tokens
   cannot express, and `[data-theme-layout="…"]` for arrangement (see [Modes and the `<html>` attributes](#modes-and-the-html-attributes)).
4. **Gate every material treatment on `[data-theme-material]`.** Flat themes — every built-in:
   `meridian`, `drift`, `schematic`, `atelier`, `elsa-cloud`, `signal`, `dusk` — must stay flat. Any rule
   consuming a `--studio-material-*` token belongs inside an `html[data-theme-material] …`
   guard.
5. **A new material token gets a neutral fallback, and recipes stay per theme.** Any new
   `--studio-material-*` token is defined in the shared `html[data-theme-material]` block as an
   alias onto the flat `--studio-*` layer; a material theme then supplies its own recipe — never
   a single hardcoded look that leaks one theme into all of them.

## The module-private alias-block pattern

A module that bundles independently (its own `module.css`) maps a small private vocabulary
onto the `--studio-*` contract in one block, then styles **only** against that private
vocabulary — no literals, no per-theme rules in the component rules. Light/dark and material
themes are inherited through whatever the aliases resolve to.

`Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/styles.css` is the exemplar: it defines an
`--otel-*` block on `.otel-page, .otel-panel`, layering primitives as fallbacks, with a single
`html[data-theme-mode="dark"]` block to adjust the private aliases for dark mode. Every
component rule references `--otel-*` only. (Its alias block is the one place that legitimately
holds literals — as fallbacks/anchors for the private aliases; see the fallback allowance in
rule 1.) `Elsa.Studio.Secrets` follows the same shape with `--studio-*`-first fallbacks.

## Stability policy

- **STABLE** — everything documented above: the `--studio-*` semantic tokens (surfaces,
  text, accent, status, type/radius), the compatibility aliases, the `--studio-material-*`
  vocabulary including the surface-role ladder (shell < panel < card < row < well). Modules may
  depend on these. Breaking changes ship only in a major version with a migration note.
- **INTERNAL** — layer-1 primitive names, the per-theme recipe *values*, the texture asset
  paths, and every module-private namespace (`--wf-*`, `--otel-*`, …). These may change at any
  time; do not depend on them across module boundaries.

## Removed in the next major version

**The glass vocabulary and the Black Glass theme.** Black Glass was the only theme that defined
`--studio-glass-*` and `--studio-blueprint-bg*`, and `html[data-theme="black-glass"]` was the only
`[data-theme]` selector module CSS was allowed. The theme is retired, so all three go together:

| Removed | Use instead |
| --- | --- |
| `--studio-glass-bg`, `-bg-strong`, `-bg-soft` | `--studio-surface`, `--studio-surface-raised`, `--studio-surface-muted` |
| `--studio-glass-row-bg`, `--studio-glass-well-bg` | `--studio-surface`, `--studio-surface-muted` |
| `--studio-glass-edge`, `-edge-strong` | `--studio-border`, and `color-mix()` of `--studio-accent` over it for emphasis |
| `--studio-glass-glow`, `-glow-strong`, `-accent-glow`, `--studio-glass-well-shadow` | `--studio-shadow-sm` |
| `--studio-glass-sheen`, `-grid`, `-texture`, `-texture-size`, `--studio-blueprint-bg`, `-bg-size` | no replacement — drop the layer, or paint over `--studio-bg` |
| `html[data-theme="black-glass"] …` rules in module CSS | delete them (Rule 3 has no exception now); the base, token-driven rules already apply |

Nothing sets `data-theme="black-glass"` any more, so a module that still carries such rules keeps
rendering correctly through its base rules: the guarded rules simply never match, and a
`var(--studio-glass-*)` outside a guard resolves to nothing — give it a fallback or replace it.
This is a breaking change to the published `@elsa-workflows/studio-ui/tokens.css` and ships with
the next major version of that package.

**The Stone, Blueprint and Brass Instrument recipes.** Their per-theme `--studio-material-*` values
were INTERNAL and are gone with the themes, along with the texture tiles under `/studio/assets/`.
The `--studio-material-*` token *names* are unchanged and now resolve to neutral fallbacks.

## Enforcement

`pnpm lint:css` runs `elsa/no-raw-color-literal` over module Client CSS
(`src/*/Client/src/**/*.css`, excluding the host shell `src/apps/Elsa.Studio.Web/**`, which owns the
theme palette). It is wired into `pnpm lint` so it runs in the same flow as ESLint. Host theme
definition files (`app/ui/tokens.css`, `app/styles.css`, `app/themes/**`) are where literals
legitimately live and are excluded.

For a genuinely non-themeable fixed palette, disable the rule around the **minimal** block
with a justification — e.g. the ConsoleStream ANSI table maps terminal SGR codes to fixed sRGB
values:

```css
/* stylelint-disable elsa/no-raw-color-literal -- ANSI SGR palette: fixed sRGB, not themeable. */
.console-stream-ansi-fg-red { color: #c0392b; }
/* stylelint-enable elsa/no-raw-color-literal */
```

(That palette is currently exempted centrally via `ignoreSelectors` in the stylelint config;
prefer an inline disable pair for any new exception you introduce.)
