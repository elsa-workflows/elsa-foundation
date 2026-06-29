# Elsa 4 Studio — UX Polish Plan

A comparative UX review of **Elsa 4 Studio** (Vite + React 19, custom `--wf-*` CSS, `@xyflow/react`) against the more polished **Elsa 3 designer** (Next.js + Tailwind + shadcn/ui), with a prioritized backlog for closing the gap.

- **Target to improve:** `elsa-foundation-studio` — workflow editor at `src/Elsa.Studio.Workflows/Client/src/styles.css` + `module.tsx`; host shell at `src/Elsa.Studio.Web/Client/src/app/`.
- **Reference:** `elsa-foundation-designer` — shadcn/Radix component layer, OKLCH tokens, sonner toasts.
- **Constraint:** This is a plan only. No application code is changed by this document.

---

## Executive summary

Elsa 4 Studio is **not** poorly built. It has a genuinely thoughtful token system (`--wf-*` soft variants via `color-mix`, a 4px spacing scale, three-tier transitions, 7 OKLCH themes — more than Elsa 3), good `aria` coverage on grids and dialogs, and node visuals (gradient fill + inset highlight + accent selection ring) that are arguably nicer than the reference. So the owner's impression — "Elsa 3 looks more polished somehow" — is real but **diffuse**: it's not one broken surface, it's the accumulation of small finish details that Elsa 3 gets for free from a mature component library and Elsa 4 hand-rolls inconsistently.

The gap concentrates in five themes, in rough order of impact:

1. **Typography "shouting."** Elsa 4 leans on `text-transform: uppercase` + `font-weight: 800` at 10–12px for *almost every* label — panel titles, section labels, `dt`, tabs, form-field labels, category toggles, port labels. This heavy, all-caps texture is the single biggest reason the UI reads as "busier / less refined" than Elsa 3, which uses sentence-case, medium-weight, 60%-opacity labels. This is a cheap, high-impact fix.
2. **Self-inflicted token bypass.** The host shell *defines* radius, shadow, and type tokens, but the workflow module ignores them — hardcoding radii (`6/7/8/10px`), font sizes (`10/11/12/13/14/20px`), and inline `rgba(15,23,42,…)` shadows that don't adapt to dark mode. Elsa 3's cohesion comes from *consistent* scales; Elsa 4 has the scales but doesn't consume them.
3. **Missing loading / empty / error finish.** Elsa 3 has skeleton rows, centered spinner-with-text, error cards (icon + title + explanation), and empty states (large muted icon + title + description + CTA). Elsa 4's `.wf-empty` is a single line of text; there are no skeletons and minimal error styling.
4. **Overlays appear/disappear instantly.** Elsa 3 animates every overlay (dialog zoom-in-95 + fade, dropdown directional slide-in, tooltip fade). Elsa 4's dialogs, connect menu, and syntax picker pop in with no transition, and panel collapse is an abrupt `display:none`. Motion is where "care" is felt.
5. **Hand-rolled controls drift.** Each button/tab/field is styled by an ad-hoc selector group, so interaction states are uneven (no active "press" feedback, focus sometimes `outline`, sometimes `box-shadow`, sometimes `outline-offset`). Elsa 3 routes everything through a few CVA variants, so every control behaves identically.

Most of the win is available in **(a) quick token-level work** with no structural change. The deeper lever — consolidating the hand-rolled controls into a small shared primitive layer — is what makes the polish *stay* consistent as the module grows.

---

## Per-surface comparison

Legend: ✅ = Elsa 4 already comparable/good · ⚠️ = noticeable gap · 🔴 = clear gap that drives the "less polished" feeling.

### 1. Workflow definition editor — canvas

| Aspect | Elsa 3 (reference) | Elsa 4 Studio | Verdict |
|---|---|---|---|
| Node visuals | Flat shadcn cards, selection from RF `selected` | Gradient fill + `inset 0 1px 0` highlight + accent selection ring (`styles.css:1487–1508`) | ✅ Elsa 4 is *nicer* here |
| Node hover | Subtle hover affordance | Only handle opacity changes (`:hover` → handle `opacity:1`), node body itself is inert | ⚠️ |
| Edge add/delete | Hover-revealed controls | `.wf-edge-actions` opacity 0→1, `0.12s` transition (`1661–1708`) | ✅ |
| Connection drag | — | No dashed preview line / drop-zone highlight while dragging | ⚠️ |
| Empty canvas | — | `.wf-empty-canvas-add` centered button with good hover (`1709–1729`) | ✅ |
| Breadcrumb | — | Raw text links, no separators (`1464–1479`) | ⚠️ |
| Reduced motion | Respected via Tailwind defaults | No `@media (prefers-reduced-motion)` anywhere | ⚠️ |

### 2. Editor — activity palette

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| List items | shadcn rows, consistent hover | `.wf-palette-activity` hover → accent-soft + grip reveals (`1388–1455`) | ✅ Good |
| Category headers | Sentence case | `font-size:11px; font-weight:800; text-transform:uppercase; letter-spacing:0.04em` (`1330–1387`) | 🔴 Over-heavy label texture |
| Collapse | Animated rail, vertical label, tooltip | Collapses to 42px via `display:none` on content — abrupt, no animation | ⚠️ |
| Tab labels | Icon + text, consistent | `11px/800/uppercase` text-only tabs (`1182–1237`) | ⚠️ |
| Drag affordance | Grip + drag ghost | Grip icon (opacity 0.45→1) but no drag ghost/drop feedback | ⚠️ |

### 3. Editor — toolbar

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| Button system | One `Button` w/ variants + sizes, active press `translate-y-px`, spinner on pending | 4+ ad-hoc selector groups (`.wf-actions button`, `.wf-toolbar button`, …) sharing one look, no press feedback (`120–149`) | 🔴 Drift-prone, flat |
| Save dirty indicator | Amber dot w/ ring on Save button | Status line banner (`.wf-status-line`, `248–281`) — functional but less glanceable | ⚠️ |
| Autosave toggle | shadcn switch | Custom `.wf-autosave-switch-input` toggle, animated knob, good focus ring (`62–118`) | ✅ Nicely done |
| Disabled buttons | `opacity-50` + pointer-events-none | `opacity:0.6/0.55` only — risks failing WCAG contrast on light bg (`145–149`, `920`) | ⚠️ |
| Tooltips | Radix tooltip, animated, `title` fallback | Native `title` only, no rich tooltips | ⚠️ |

### 4. Editor — inspector / properties / expression / code

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| Section grouping | `section-card.tsx`: collapsible, tinted icon squares (sky/amber/emerald), hover shadow | Flat `.wf-properties` groups, `dl/dt/dd`, no collapsible tinted sections (`2083–2178`) | ⚠️ Reference is richer/scannable |
| Field labels | Sentence case, medium | `dt` + property labels at `font-weight:800; uppercase` (`1844–1866`, `2128`) | 🔴 Heavy |
| Inputs | One `Input`, consistent focus ring (3px/50%) | `.wf-property-row input` focus = border + `0 0 0 2px` mix — fine but a *different* recipe than form-field/dialog inputs (`2149–2178`) | ⚠️ Inconsistent focus recipes |
| Expression field | — | `.wf-expression-field` inline syntax picker, expand button, expand dialog — well-built (`2180–2474`) | ✅ Strong |
| Syntax/connect menus | Animated zoom+fade + slide-in | `.wf-syntax-picker-menu` / `.wf-connect-menu` appear instantly, no enter animation (`1731–1819`, `2180–2299`) | ⚠️ |
| Resize handle | Sky pill, hover/drag color, dbl-click reset | `.wf-side-resize-handle::before` pill, hover→accent, focus ring (`1279–1328`) | ✅ Comparable |

### 5. Definitions list

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| Container | `rounded-xl border shadow-xs ring-1 ring-black/[0.02]` — barely-there depth | `.wf-grid` border + radius, no ring/shadow layering (`332–366`) | ⚠️ Flatter |
| Row hover/select | `hover:bg-muted/50`, `data-[state=selected]` | `:hover`→accent-soft, `[aria-selected]`→accent 14% mix, focus outline (`707–735`) | ✅ Good |
| Header labels | Sentence/medium | `11px/700/uppercase` (`1074–1080`) | ⚠️ |
| Status | `Badge` variants (Published/Draft) | `.wf-status-badge` semantic by `data-status` (`672–705`) | ✅ Comparable |
| Loading | 6 skeleton rows matching real height | None — blank while loading | 🔴 |
| Empty | Centered `size-14` icon + title + description + CTA | `.wf-empty` = one muted text line (`1289–1293`) | 🔴 |
| Pagination | shadcn controls | `.wf-pagination` summary + controls, disabled states (`860–929`) | ✅ Good |

### 6. Instances list & instance viewer

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| Layout | Split canvas + inspector | `.wf-instance-detail-workbench` 2-col grid, canvas + inspector (`393–454`) | ✅ Solid structure |
| Activity/incident lists | — | `.wf-instance-activity` w/ selected `inset 3px 0 0 accent`, severity-tinted incidents (`549–633`) | ✅ Good semantic color |
| Status badges | Badge variants | `data-status` running/completed/faulted/suspended (`672–705`) | ✅ |
| Unmatched evidence | — | Dashed-border cards (`609–633`) | ✅ Nice touch |
| Loading/empty/error | Skeletons + error cards | Minimal — same `.wf-empty`/`.wf-alert` text rows (`228–281`) | 🔴 |

### 7. Cross-cutting: feedback & motion

| Aspect | Elsa 3 | Elsa 4 | Verdict |
|---|---|---|---|
| Toasts | sonner w/ per-type lucide icons, theme-aware | No toast system surfaced; relies on inline status banners / themed dialogs | ⚠️ |
| Dialog motion | Backdrop fade + popup zoom-in-95, 100ms | `.wf-dialog`/`.wf-property-editor-dialog` appear instantly (`931–1058`, `2351–2474`) | 🔴 |
| Focus rings | One recipe everywhere (3px ring/50%) | Mix of `outline`, `box-shadow`, `outline-offset` per element | ⚠️ Inconsistent |
| Reduced motion | Honored | Absent | ⚠️ |

### Cross-cutting: host shell (`Elsa.Studio.Web/Client`)

The shell is genuinely strong and **not** a major source of the gap: backdrop-blur sticky topbar (`422–498`), nav active-indicator bar (`.nav-section a::before`, `1715–1741`), tinted `.nav-icon-tile`, status-dot footer with glow ring, a real spacing/radius/shadow/transition token set (`tokens.css`), and a 7-theme OKLCH system (`themes/presets.ts`). The irony: **the shell's tokens are the fix** for the workflow module's hardcoding.

---

## Prioritized backlog

### (a) Quick wins — token-level tweaks (hours each, no structural change)

| # | Problem | Proposed change | Files / tokens | Effort |
|---|---|---|---|---|
| A1 | **Label "shouting"** — uppercase + 800-weight at 10–12px on nearly every label makes the UI read busy/heavy | Soften the label recipe: sentence case **or** drop to `font-weight:600`, `text-transform:none`, color `--wf-muted-text` at ~70%. Apply to one shared `.wf-label`/`.wf-section-label` rule and have panel titles, `dt`, tabs, category toggles, form-field labels reference it | `Workflows/.../styles.css`: `.wf-panel-title` (`1168`), `.wf-section-label`/`.wf-properties` (`2083–2128`), `.wf-inspector dt` (`1844`), `.wf-panel-tab-list button` (`1182`), `.wf-palette-category-toggle` (`1330`), `.wf-form-field` (`974`), `.wf-grid-head` (`1074`), `.wf-instance-meta dt`. New token `--wf-label-weight`, `--wf-label-transform` | M |
| A2 | Hardcoded radii (`6/7/8/10px`) don't track the shell's radius scale | Replace literals with `--radius-sm`/`--radius`/`--radius-md`/`--radius-xl` (defined in `Web/Client/.../tokens.css`/`app/styles.css:8–13`). Add `--wf-radius-*` aliases on `.wf-page`/`.wf-editor` mapping to host tokens | `Workflows/.../styles.css` (global) — reuse existing `--radius-*` | S |
| A3 | Inline `rgba(15,23,42,…)` shadows on nodes/dialogs don't adapt to dark mode | Define `--wf-shadow-node`, `--wf-shadow-overlay`, `--wf-shadow-menu` on `.wf-editor`, derived from host `--shadow-md/lg/xl` (`app/styles.css:23–27`); reference them in `.wf-node`, `.wf-dialog`, `.wf-syntax-picker-menu`, `.wf-property-editor-dialog` | New `--wf-shadow-*` tokens; reuse `--shadow-*` | S |
| A4 | Hardcoded font sizes (`10/11/12/13/14/20px`) → unbounded type scale | Map to the shell type tokens (`--studio-body-size` 13, `--studio-label-size` 12, `--studio-section-title-size` 16, `--studio-page-title-size` 20) plus one new `--wf-text-2xs` (10px, for port labels/badges) — mirrors Elsa 3's bounded `text-2xs→base` scale | `tokens.css:30–33`; new `--wf-text-2xs`; apply across `styles.css` | M |
| A5 | Activity icon colors hardcoded (`#10b981/#0284c7/#d97706/#f472b6`) → won't follow theme | Promote to `--wf-activity-terminal/composite/runtime/trigger` tokens on `.wf-editor`, defaulting to current hex; lets themes override | `styles.css:1524–1630`; `module.tsx:3327–3342` (`renderActivityIcon`) | S |
| A6 | Inconsistent focus recipe (outline vs box-shadow vs offset) | Adopt one ring recipe: `box-shadow: 0 0 0 3px color-mix(in srgb, var(--wf-accent) 40%, transparent)` via a shared `.wf-focusable:focus-visible` or a CSS custom-property mixin; reconcile `.wf-grid-row` (`711`), `.wf-side-resize-handle` (`1320`), form inputs (`1011`, `2173`), autosave toggle (`115`) | `styles.css` (focus rules) | S |
| A7 | Disabled buttons use opacity only (`0.55–0.6`) → possible WCAG-AA contrast fail | Add an explicit disabled color token (`--wf-disabled-text`) instead of relying on opacity; keep `cursor:not-allowed` | `styles.css:145–149`, `920–923`, `1264–1267` | S |
| A8 | No reduced-motion support | Add a global `@media (prefers-reduced-motion: reduce) { * { transition-duration: 0.01ms !important; animation: none !important; } }` block | `Workflows/.../styles.css` + `app/styles.css` | S |
| A9 | Breadcrumbs are bare text links | Add a `/` or chevron separator (lucide `ChevronRight` already imported) and `--wf-muted-text` styling | `module.tsx` breadcrumb render; `styles.css:1464–1479` | S |

### (b) Component-level polish (0.5–2 days each)

| # | Problem | Proposed change | Files | Effort |
|---|---|---|---|---|
| B1 | **No skeleton loaders** — grids/editor show blank while loading | Add a `.wf-skeleton` rule (`animate-pulse`-equivalent: `background: linear-gradient` shimmer or opacity keyframe) and render N skeleton rows at real row height for definitions/instances; a centered spinner-with-text for the editor (mirror Elsa 3 `definition-editor.tsx` loading) | `styles.css` (new `.wf-skeleton`); list render sites in `module.tsx` | M |
| B2 | **Weak empty states** — `.wf-empty` is one line | Define a shared `.wf-empty-state` (centered, `~48px` muted icon tile + title + description + optional CTA). Apply to empty definitions list, empty instances list, empty canvas. Use existing lucide icons | `styles.css:1289`; render sites in `module.tsx` | M |
| B3 | **No real error states** | Add `.wf-error-card` (destructive icon + bold title + muted explanation), replacing bare `.wf-alert` text for load failures in editor + instance viewer | `styles.css:228–281`; `module.tsx`, `ActivityPropertiesPanel.tsx` | M |
| B4 | **Overlays pop in with no motion** | Add enter/exit animation to `.wf-dialog`, `.wf-property-editor-dialog`, `.wf-syntax-picker-menu`, `.wf-connect-menu`: backdrop `fade-in`, popup `zoom-in(0.97→1)` + fade over `120ms`. Pure CSS keyframes, gated by reduced-motion (A8) | `styles.css:931–1058`, `1731–1819`, `2180–2474` | M |
| B5 | **Button drift** — many ad-hoc button selectors, no press feedback | Consolidate into a small set of utility classes: `.wf-btn` (base) + `.wf-btn--primary/--ghost/--danger` + `.wf-btn--sm`. Add `:active { transform: translateY(1px); }` press feedback. Migrate toolbar, row-actions, pagination, dialog-actions to them | `styles.css:120–149`, `825–859`, `860–929`, `1028–1057` | L |
| B6 | Panel collapse is abrupt `display:none` | Animate the grid-template-column width transition (`--wf-palette-width`/`--wf-inspector-width`) and fade content opacity instead of hard hide | `styles.css:1106–1277`; `module.tsx` collapse logic | M |
| B7 | Node body has no hover state (only handles react) | Add `.wf-node:hover` subtle lift: `border-color` toward accent + slightly stronger `--wf-shadow-node`. Keep distinct from `.selected` | `styles.css:1487–1508` | S |
| B8 | No toast/confirmation feedback for save/publish/run | Introduce a lightweight toast (port the host-shell themed-dialog system or a minimal `.wf-toast` region) with success/error/info variants + lucide icons, mirroring sonner usage | New component in `module.tsx`/host; `styles.css` | L |
| B9 | List container reads flat vs Elsa 3's layered depth | Add `box-shadow: var(--wf-shadow-sm)` + a hairline `ring`-equivalent (`box-shadow: inset 0 0 0 1px color-mix(...border... 2%)`) to `.wf-grid`, `.wf-pagination`, `.wf-runtime-card` | `styles.css:332`, `860`, `1868` | S |

### (c) Larger structural / interaction improvements (multi-day)

| # | Problem | Proposed change | Files | Effort |
|---|---|---|---|---|
| C1 | **Root cause of drift:** every control is hand-styled, so states stay uneven as the module grows | Extract a small shared primitive layer for the wf module — `Button`, `Field/Input`, `Badge`, `Menu`, `Dialog`, `Tabs` — each owning its states/animations once (the role shadcn plays for Elsa 3). Can be thin React wrappers over the existing CSS classes from B4/B5. This is what makes A1–B9 *stay* consistent | `Workflows/Client/src/` (new `ui/` dir); refactor `module.tsx` call sites | XL |
| C2 | Properties panel is a flat list vs Elsa 3's collapsible tinted section cards | Adopt a `.wf-section-card` pattern: collapsible header, tinted icon square per group (input/output/runtime), hover shadow, chevron rotation. Improves scannability of dense property forms | `styles.css:2083–2178`; `ActivityPropertiesPanel.tsx` | L |
| C3 | No connection-drawing feedback | Add a dashed preview edge that follows the cursor while dragging a connection, and highlight valid target handles/drop slots | `module.tsx` (xyflow connection handlers); `styles.css` handle rules `1632–1659` | L |
| C4 | Drag-and-drop from palette lacks feedback | Add a drag ghost element + valid drop-zone highlight on the canvas when dragging an activity from the palette | `module.tsx` DnD; `styles.css` palette/canvas | L |
| C5 | Loading transitions are binary (blank → full) | Orchestrate skeleton→content cross-fade for editor and lists once B1 lands, so data arrival feels smooth rather than a snap | `module.tsx` data-loading sites | M |

---

## Suggested sequencing

**Phase 1 — "Calm the texture" (≈2–3 days, highest perceived impact).**
Do the token-level quick wins that change the *feel* immediately: A1 (label softening) → A4 (bounded type scale) → A2/A3 (radius + shadow tokens) → A6 (one focus recipe) → A7/A8/A9. Outcome: the UI stops "shouting" and gains the quiet consistency that reads as polish — the biggest lever for the owner's specific complaint, with zero structural risk.

**Phase 2 — "Never show a void" (≈3–4 days).**
B1 (skeletons) → B2 (empty states) → B3 (error cards) → B9 (list depth). These are the states Elsa 3 conspicuously handles and Elsa 4 doesn't; they're where polish is most *noticed* because they appear on every page load.

**Phase 3 — "Add the motion & finish" (≈3–5 days).**
B4 (overlay animations) → B7 (node hover) → B6 (panel collapse) → B5 (button consolidation + press feedback) → B8 (toasts). Accumulated micro-interactions are the "somehow" in "looks more polished somehow."

**Phase 4 — "Make it stay polished" (structural, schedule deliberately).**
C1 (shared primitive layer) first — it absorbs and standardizes everything from Phases 1–3 — then C2 (section cards), then the canvas interaction upgrades C3/C4/C5 as feature work allows.

### Token work summary

- **Reuse existing host tokens:** `--radius-sm/--radius/--radius-md/--radius-xl` (A2), `--shadow-sm/md/lg/xl` (A3), `--studio-*-size` type tokens (A4), `--transition-fast/base/slow` (already used). The shell already defines these in `Elsa.Studio.Web/Client/src/app/ui/tokens.css` and `app/styles.css` — the workflow module just needs to consume them.
- **Add new `--wf-*` tokens:** `--wf-text-2xs` (A4), `--wf-shadow-node/overlay/menu` (A3), `--wf-activity-terminal/composite/runtime/trigger` (A5), `--wf-label-weight`/`--wf-label-transform` (A1), `--wf-disabled-text` (A7), `--wf-radius-*` aliases if a local mapping is cleaner than referencing host tokens directly (A2).

---

## Honest notes — where Elsa 4 is already good

So the plan isn't read as "everything is broken":

- **Node rendering** (gradient + inset highlight + accent selection ring) is more refined than Elsa 3's flat cards.
- **Theme system** is broader — 7 OKLCH presets with light/dark, vs Elsa 3's single palette.
- **`color-mix` soft-variant strategy** (`--wf-accent-soft` etc.) is elegant and theme-adaptive — keep it; the gaps above build *on* it.
- **Host shell** (backdrop-blur topbar, nav active bar, tinted icon tiles, status-dot footer) is already polished and barely needs work.
- **Resize handles, autosave toggle, expression field + syntax picker, instance activity/incident lists with severity tinting** are all well-executed.
- **Accessibility baseline** (grid `role`s, `aria-selected`, `aria-live` pagination, dialog `aria-modal`, Escape handling, semantic `dl/dt/dd`) is solid — the focus-ring *consistency* (A6) is the main refinement needed, not coverage.

The takeaway: Elsa 4's foundation is sound. The polish gap is finish work — taming heavy labels, consuming its own tokens, and adding the loading/empty/error/motion details — not a rebuild.
