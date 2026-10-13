# Studio UX design spec

Status: draft for the Elsa 3 → Elsa 4 parity series. This spec is the yardstick each UX PR is
reviewed against. It builds on [design-tokens.md](../design-tokens.md) (the token contract) and
[ux-polish-plan.md](../ux-polish-plan.md) (the per-surface backlog). Where the plan and this spec
disagree, this spec wins for new work, and the plan should be updated in the same PR.

## Status legend

Every rule is tagged. Reviewers must not treat a **target** as something to verify in today's
code.

| Tag | Meaning |
|---|---|
| `exists` | True on `main` today. |
| `target, phase N` | Not true yet. `N` is the phase in [ux-polish-plan.md](../ux-polish-plan.md) (1 texture, 2 empty/error, 3 motion/finish, 4 structural). Settings › Appearance, System as a pickable mode, and Reset now `exists`. |

## Principles

1. **Content first.** `target, phase 1` Chrome (toolbars, docks, widget controls) is hidden until
   it is needed. A page opens on its content, not on a diagnostic panel.
   **Check:** on a cold load (no stored chrome keys) of a list or dashboard page, the content
   region (table, grid or canvas) is in view; the bottom dock is collapsed; dashboard widget
   action chrome is not visible until hover or focus. Fail if an open dock, diagnostic panel or
   always-visible widget toolbar is the first thing on screen.
2. **One primary action per view.** `exists` on list and detail pages as the review rule;
   Designer is an exception (see Layout). Exactly one filled (`primary`) button per page header;
   everything else is `default`, `ghost` or in an overflow menu. The Designer header keeps Save,
   Review & publish and Run visible. Today Run is the filled action; making Review & publish the
   one `primary` (Save and Run `default` or `ghost`) is `target, phase 1`.
3. **Tokens only.** Colour, radius, shadow, spacing and type come from `--studio-*` tokens.
   Colour lint is only partly true today: `pnpm lint:css` (`elsa/no-raw-color-literal`) covers
   `src/essentials/*/Client/src/**/*.css` and `Extensions/*/studio/*/Client/src/**/*.css`.
   `.stylelintrc.json` ignores `**/Elsa.Studio.Web/Client/**`, so the app shell, `ui/forms`,
   `ui/layout`, `ui/feedback` and `app/styles.css` are **not** colour-linted. The disclosure kit
   CSS (`ui/disclosure/**/*.css`) is linted via `.stylelintrc.disclosure.json`. Module colour:
   `exists`. Shell/`ui` colour: review-only until coverage is extended (`target, phase 4`).
   Radius, shadow and type: `target, phase 1`.
   **Check:** the diff uses only `--studio-radius-sm`, `--studio-radius`, `--studio-radius-md`,
   `--studio-radius-lg` and `--studio-shadow-sm` (see [design-tokens.md](../design-tokens.md)).
   Fail if changed CSS adds a raw `px` radius, a hex/rgba `box-shadow`, or a token outside that
   set. Type sizes must be the `--studio-*-size` tokens from the same contract.
4. **Every theme, every mode.** `exists` as a review rule. Each change is checked in Classic
   Light and one dark theme, and must not break Dim or High contrast. Concrete modes are `light`,
   `dark`, `dim` and `high-contrast`. `system` exists as a stored preference (follows the OS, live)
   and is the default. Settings › Appearance offers System; the ThemeSwitcher still lists the
   four concrete modes (`exists`).
5. **Quiet typography.** `target, phase 1` Sentence case for labels, tabs and panel titles.
   Uppercase mono is reserved for page eyebrows and table headers.

## Layout

| Region | Status | Rule |
|---|---|---|
| Shell | `exists` | Sidebar or top menubar (theme or user choice). The top bar holds the page title, global actions and the theme control (`ThemeSwitcher`). |
| Navigation placement | `exists` | Each nav contribution may declare `placement` (`main` \| `more` \| `settings` \| `admin`), `order`, `requiresFeature` and `requiresRole` / `requiresPermission`. Unset `placement` is `more` at the top level; a child inherits its parent unless it declares a different placement, in which case it renders there. Main holds Home, Workflows, Runs and Tasks (Tasks only when the user-tasks feature is enabled). Extensions land under **More tools** (open state in `elsa-studio.ui.nav.more`). `settings` items (Appearance) render under Studio in the Settings shell and stay visible to every session. `admin` items render under Administration and use the host-control read gate. The sidebar keeps one Settings entry; searching still reveals the individual sections. Anonymous/demo sessions still see Administration when sign-in is off. Disabled features contribute nothing; role-gated items are hidden. Home points at the existing dashboard/home route until the customizable Home lands. |
| Settings shell | `exists` | `/settings` is a two-pane shell (section list + content). Sections are deep-linkable as `/settings/<id>`. Original admin paths (`/modules`, `/theme-builder`, …) open the same shell and the same gate; a signed-in user without host-control read sees those sections as unavailable. The route is lazy-loaded. |
| User menu | `target, phase 4` | See [User menu and sign-out](#user-menu-and-sign-out). Live has none. |
| Bottom dock | `exists` | Remembers the user's last state in `elsa-studio-bottom-panel-collapsed`, `elsa-studio-bottom-panel-height`, `elsa-studio-bottom-panel-maximized` and `elsa-studio-active-bottom-panel`. With no stored value the dock is **open** on desktop (`collapsed=false`) and collapsed at ≤640px. |
| Bottom dock default | `target, phase 1` | Collapsed by default when no stored value, including on desktop. |
| Bottom dock errors | `target, phase 2` | A status dot on the collapsed dock signals new errors. |
| Page | `exists` | Content sits on the page surface. No wrapping card around a whole page. |
| Page header | `target, phase 1` | Eyebrow, title, one-line description, actions. One `primary` button (see Principles). |
| Lists | `target, phase 2` | A single filter row (search + segmented status + "More filters"), then the grid, then the pager. Filters apply live. |
| Designer | `exists` | Palette / canvas / inspector. |
| Designer tabs | `target, phase 1` | Tab labels never truncate: they collapse to icon + tooltip below the width they need. |
| Designer header | `exists` | Save, Review & publish and Run are all visible. None uses `StudioButton`. Run is the filled action (`.wf-run-button` uses `--studio-accent`). Review & publish is outlined (`.wf-review-button`). |
| Designer header primary | `target, phase 1` | Review & publish becomes the one `primary` action; Save and Run become `default` (or `ghost`). |

## Components

| Component | Status | Rule |
|---|---|---|
| Button (today) | `exists` | `ui/forms/Button` (`StudioButton`): variants `primary`, `default`, `ghost`; sizes `sm` / `md`. Focus ring is `--studio-focus` (there is no `--studio-ring`). |
| Button (additions) | `target, phase 3` | Pressed state (`:active` translate) and a spinner while pending. |
| Button (new variants) | `target, phase 4` | `outline` and `destructive` variants. Until they exist, use `default` / `ghost` and do not name `outline` or `destructive` in new call sites. |
| Tabs | `exists` | `ui/layout/Tabs` (`StudioTabs`). Sentence case; overflow collapse is `target, phase 1`. |
| DataGrid | `exists` | `ui/data-grid/DataGrid`: row click (or Enter/Space) opens. |
| DataGrid extras | `target, phase 4` | Sortable headers with an indicator; optional column picker persisted per grid; secondary actions in a ⋯ menu. |
| Status | `exists` | `StatusChip` (filled) for run and definition status; `StatusPill` (outline) for low-emphasis metadata. Colours from the status tokens only. |
| Feedback | `exists` (primitives) / `target, phase 2` (rich pattern) | `EmptyState`, `SkeletonRows` and `StudioAlert` exist. The richer pattern — icon, title, one-line hint, optional action — is the phase 2 target. |
| Overlays | `target, phase 3` | Popovers and dialogs animate in (fade + 4px slide/scale, ≤ 150ms) and honour `prefers-reduced-motion`. |
| Icons | `exists` | lucide, 16px in controls, 20px in nodes and tiles. |
| Disclosure kit | `exists` | `ui/disclosure/*`: `AddOption`, `RevealToggle`, `AdvancedSection`, `TechnicalDetails`, `OverflowMenu`, `ExpressionToggle`, `PeerViewSwitch`. Shared `persistKey` / `forceOpen`. A non-default, bound or invalid field is never hidden. Motion ≤ 150ms via `--studio-disclosure-duration`, honours `prefers-reduced-motion`. Keyboard + `aria-expanded` / `aria-controls`. Fixture at `/dev/disclosure` in dev builds only (`import.meta.env.DEV`) and `tests/browser/disclosure.html`. Not adopted on inspector, settings or other surfaces yet. |

## Appearance

The appearance model is `app/themes/appearance.ts` (`exists`, landed in #2710). ThemeProvider
owns the live state; this module owns the shape, defaults, persistence and resolution.

| Preference | Status | Values | Default | Stored as |
|---|---|---|---|---|
| Theme | `exists` | any selectable theme id | configured `Themes:DefaultThemeId`, else the first theme | `elsa-studio-theme` |
| Mode | `exists` | `system`, `light`, `dark`, `dim`, `high-contrast` | `system` (follows OS colour scheme and contrast, live) | `elsa-studio-theme-mode` |
| Navigation | `exists` | `theme`, `left`, `top` | `theme` | `elsa-studio-nav-mode` |

- The topbar `ThemeSwitcher` edits theme, the four concrete modes and nav today (`exists`).
  Settings › Appearance (`exists`) edits the same model: theme (Classic default, plus the other
  built-in and custom themes), mode (System, Light, Dark, and the theme's optional modes),
  navigation, and a link to the Theme Builder. Density is not in the model. Neither surface
  keeps its own state.
- `resetAppearancePreferences()` / `resetAppearance` exist (`exists`) and **Reset appearance**
  on Settings › Appearance exposes them. Reset clears these eight keys (`exists`):
  - `elsa-studio-theme`
  - `elsa-studio-theme-mode`
  - `elsa-studio-nav-mode`
  - `elsa-studio-sidebar-collapsed`
  - `elsa-studio-bottom-panel-collapsed`
  - `elsa-studio-bottom-panel-height`
  - `elsa-studio-bottom-panel-maximized`
  - `elsa-studio-active-bottom-panel`
  It does **not** clear editor layout keys (palette, inspector, run-detail widths).
- A theme that lacks the chosen mode renders its nearest supported mode, but the preference is
  kept (`exists`: `preferredMode` vs `mode` in `ThemeProvider`).
- Theme definitions (Theme Builder, theme store) are admin-level and server-side; appearance
  preferences are per browser (`exists`).

## User menu and sign-out

`target, phase 4`. Live Studio has no user menu. The topbar today is title, Weaver (when
enabled), two GitHub links and `ThemeSwitcher`.

- Place the user menu on the topbar, after the theme control.
- Contents: signed-in display name (or account id) and **Sign out**.
- Sign out calls `useAuthContext().logout()` (`AuthProvider.logout` already exists; only the
  menu is missing).
- When no auth provider is configured, omit the menu.

## Toasts vs banners

| Kind | Status | Use for |
|---|---|---|
| Inline banner | `exists` | Durable, in-place failures that stay until the cause is fixed or the user dismisses them: `StudioAlert`, `WorkflowAlert`, page status banners. |
| Toast | `target, phase 3` | Transient success or info (save, publish, run started). One toast region; honour `prefers-reduced-motion`. |

Do not fire a toast and a banner for the same event. Page-level load failures stay banners;
completed actions become toasts.

## Designer canvas items

| Item | Status | Notes |
|---|---|---|
| Minimap | `exists` | `MiniMap` on `GraphAuthoringCanvas` (and run / executable canvases). |
| Fit / zoom | `exists` | xyflow `Controls` on the same canvases. |
| Empty-canvas add | `exists` | `.wf-empty-canvas-add`; editorial layout also has **Add step**. |
| Save-state indicator | `exists` | `.wf-editor-save-status` in the Designer header (pending spinner / saved check). |
| Node toolbar | `target, phase 3` | Hover/selection toolbar on the node (not a permanent chrome bar). |

## Dashboard widget chrome

`exists`: `WidgetFrame` always shows move, size, settings, refresh and hide in
`.dashboard-widget-actions`.

`target, phase 1`: those controls are hidden until the widget is hovered or focused. Title,
description, pinned mark and body stay visible (content first).

## Activity icons

`exists`: one resolver, `resolveActivityIcon` in `activityIcons.ts`, used by the designer
palette, canvas nodes, and the run-view timeline. Order: catalog `icon` kind token, then
catalog `iconName` (a mapped lucide export or kebab name), then the activity type key, then
the category icon, then `executionType === trigger`. An `icon` / `iconName` that is not a
known kind and not in the lucide map is ignored so the later steps still run.
`renderActivityIcon` (in `activityIconView.tsx`) lazy-loads the lucide table from
`activityIconGlyphs.tsx` so the Workflows entry and landing paths stay inside the bundle
budget; a rejected chunk load keeps the placeholder. Extensions call
`loadActivityIconRegistry` then `registerActivityIconGlyphs`.
Palette rows and canvas nodes set `data-tone` from `resolveActivityIconTone` and colour
through `--wf-activity-*`, which honour the host override `--studio-wf-activity-*` and then
`--studio-success` / `--studio-accent` / `--studio-warning` / `--studio-danger`.

`target, phase 4`: `iconColor` from the catalog is still unused; category tone wins so icons
track the theme instead of a per-descriptor hex.

## Review checklist for UX PRs

- [ ] One surface; no unrelated refactors.
- [ ] Before/after screenshots at 1600×1000 @2x, Classic Light and one dark theme.
- [ ] Only `--studio-*` tokens. `pnpm lint:css` passes for module CSS it actually covers
      (`essentials` and `Extensions` Client CSS, plus `ui/disclosure/**/*.css`). Shell / other
      `ui` colour is review-only until `Elsa.Studio.Web/Client` is no longer in
      `.stylelintrc.json` `ignoreFiles`.
- [ ] Keyboard and focus behaviour unchanged or better.
- [ ] Axe: `pnpm test:browser -- expression-code-intelligence-accessibility.spec.ts` (Chromium
      project in `Studio/playwright.config.ts`) passes (it fails on any critical/serious
      violation). A new surface adds a sibling spec under `Studio/tests/browser/` that injects
      `axe-core` the same way. `Client/src/__tests__/a11y.test.tsx` is keyboard/ARIA unit
      coverage, not axe.
- [ ] Checked against ux-polish-plan.md; plan updated if the item is now done.
- [ ] Each claimed behaviour is tagged `exists` or `target, phase N` in this spec; the PR does
      not "verify" a target against code that is not there yet.
