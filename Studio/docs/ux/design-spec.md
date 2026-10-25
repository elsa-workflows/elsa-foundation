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
| Shell | `exists` | Sidebar or top menubar (theme or user choice). The top bar holds the page title, global actions, the theme control (`ThemeSwitcher`) and, when sign-in is on, the user menu. |
| Navigation placement | `exists` | Each nav contribution may declare `placement` (`main` \| `more` \| `settings` \| `admin`), `order`, `requiresFeature` and `requiresRole` / `requiresPermission`. Unset `placement` is `more` at the top level; a child inherits its parent unless it declares a different placement, in which case it renders there. Main holds Home, Workflows, Runs and Tasks (Tasks only when the user-tasks feature is enabled). Extensions land under **More tools** (open state in `elsa-studio.ui.nav.more`). `settings` items (Appearance) render under Studio in the Settings shell and stay visible to every session. `admin` items render under Administration and use the host-control read gate. The sidebar keeps one Settings entry; searching still reveals the individual sections. Anonymous/demo sessions still see Administration when sign-in is off. Disabled features contribute nothing; role-gated items are hidden. Home is the customizable landing page at `/`, `/dashboard` and `/overview`; there is no separate Dashboard entry. |
| Settings shell | `exists` | `/settings` is a two-pane shell (section list + content). Sections are deep-linkable as `/settings/<id>`. Original admin paths (`/modules`, `/theme-builder`, …) open the same shell and the same gate; a signed-in user without host-control read sees those sections as unavailable. The route is lazy-loaded. |
| User menu | `exists` | See [User menu and sign-out](#user-menu-and-sign-out). |
| Bottom dock | `exists` | Remembers the user's last state in `elsa-studio-bottom-panel-collapsed`, `elsa-studio-bottom-panel-height`, `elsa-studio-bottom-panel-maximized` and `elsa-studio-active-bottom-panel`. With no stored value the dock is **open** on desktop (`collapsed=false`) and collapsed at ≤640px. |
| Bottom dock default | `target, phase 1` | Collapsed by default when no stored value, including on desktop. |
| Bottom dock errors | `target, phase 2` | A status dot on the collapsed dock signals new errors. |
| Page | `exists` | Content sits on the page surface. No wrapping card around a whole page. |
| Page header | `target, phase 1` | Eyebrow, title, one-line description, actions. One `primary` button (see Principles). |
| Lists | `target, phase 2` | Keep the page heading/actions, primary search or filters, results, and pager in a stable order. Use an accessible table or grid with a clear empty/error state. Lists may retain domain-specific filter layouts; advanced Runs criteria remain drafts until explicit Apply and applied criteria stay visible as removable chips outside the disclosure. See [Definition list consistency](#definition-list-consistency-3070). |
| Runs | `exists` for the reference implementation | Workflow names lead, with exact execution, artifact, definition and version IDs available as copyable identifiers. Bulk actions appear only after row selection and preserve the host's permission gates. Detail navigation returns to the validated local list query, including page size and cursor. Loading, empty, unsupported and request-error states stay distinct; failed requests offer Retry. |
| Designer | `exists` | Palette / canvas / inspector. |
| Designer tabs | `exists` for inspector / Runtime / Artifacts panel tabs (wrap + `title`, no ellipsis); remaining collapse-to-icon is `target, phase 1` | Tab labels never truncate. The inspector panel tabs wrap and keep a `title` plus keyboard access. Below the width they need they should collapse to icon + tooltip. |
| Designer header | `exists` | Save, Review & publish and Run are all visible. None uses `StudioButton`. Run is the filled action (`.wf-run-button` uses `--studio-accent`). Review & publish is outlined (`.wf-review-button`). |
| Designer header primary | `target, phase 1` | Review & publish becomes the one `primary` action; Save and Run become `default` (or `ghost`). |

## Definition list consistency (#3070)

**Status: `exists`** (accepted in [#3070](https://github.com/elsa-workflows/elsa-foundation/issues/3070)). The issue record carries the acceptance and delivery evidence.

The Activity Definitions and Workflow Definitions collections use the same scan hierarchy:
page inset, eyebrow/title/description, one filled primary action, secondary page actions, search
and filters, results, and pagination. Keep meaningful table headings and row labels. Shared
hierarchy is a presentation rule, not a shared data-grid or filtering framework.

- **Exact identity:** Keep each row's human-readable name and one exact, copyable primary ID
  visible by default. Put additional technical IDs in the existing keyboard-accessible
  `AdvancedSection`, with exact copy values and display-only truncation. A copy or disclosure
  action must not open its containing row. A row reacts to Enter or Space only when the row
  itself is the event target. In Workflow Definitions, previous search results retained while a
  new query is pending stay non-actionable and do not expose their secondary identifiers.
  Activity Definitions keeps its same-authority live-search placeholder rows and their existing
  interactions while a replacement query is pending.
- **Activity Definitions exception:** Keep the custom Activity collection and its live search,
  Designed/Built-in/All authority filter, provider filter, page size, and server-owned opaque
  cursor contract. The cursor stays in history state and never appears in a newly invented URL
  parameter. Workbench/editor return restores the collection entry that opened it, including
  entered and settled filters and its cursor stack. An authorization or backend change redacts
  old rows and restarts paging while preserving filters and page size. Invalid continuation and
  expired snapshot recovery remain distinct from a generic request failure.
- **Workflow Definitions exception:** Preserve lifecycle tabs, folder and Unfiled scope,
  selection, move, delete, restore, permanent-delete confirmations, and the current browse-session
  behavior. Folder navigation and Move require the host's advertised relations; the normal Workbench does not currently provide them. Bookmarked unsupported folder scopes show a truthful fallback rather than guessed requests. Secondary IDs remain unavailable while stale previous-query rows are displayed. At narrow widths, constrain Name-cell identifiers and complete copy-button hitboxes to the visible scroll container, including the shell content inset; retain horizontal access to later table columns.
- **Runs exception:** Keep workflow name as the first scan target and the exact execution ID
  visible and copyable. Reveal artifact, definition, and version IDs on demand. Keep applied and
  advanced filters, cursor navigation, row selection, and capability-gated deletion unchanged.
- **Result states:** Keep loading, empty, request failure, denied access, unavailable capability,
  stale retained data, invalid continuation, and expired snapshot visibly distinct. Do not expose
  identities or counts after a confirmed privacy denial.

## Resource list consistency (#3071)

**Status: `exists`** (accepted in [#3071](https://github.com/elsa-workflows/elsa-foundation/issues/3071)). The issue record carries the acceptance and delivery evidence.

Secrets and Connections follow the definition lists' page inset, heading/description, one filled
header create action, search/filter toolbar, and results order. Reuse the existing Studio controls
and token contract while keeping each resource's domain columns and operations:

- **Secrets:** Edit stays directly available per row. Rotate, Test, Revoke and Delete use the
  keyboard-accessible secondary menu, with destructive actions separated. Copy uses only the
  technical name. Preserve masking, operation permissions, confirmations and focus return.
- **Inventory truth:** One internal 100-record limit governs both the Secrets request and
  coverage copy. Server search and total counts stay authoritative; Type/Store/Scope/Status
  facets cover loaded rows only. An absent selected scope stays qualified. Draft, accepted and
  failed attempted queries keep their separate meanings.
- **Connections:** Details remains the row entry; Test, Revoke and Delete remain in Details.
  Creation retains the accepted query, announces the exact created name and reloads that query.
  Select the created item only if its exact name is returned. Test describes local readiness;
  Revoke describes Elsa-side status.
- **Home destination:** Only Needs attention requires effective `workflows/runtime:read` for
  authenticated sessions. Preserve its active-incident Runs URL, authentication-disabled
  behavior and the complete authorized Continue catalog.

## Components

| Component | Status | Rule |
|---|---|---|
| Button (today) | `exists` | `ui/forms/Button` (`StudioButton`): variants `primary`, `default`, `ghost`; sizes `sm` / `md`. Focus ring is `--studio-focus` (there is no `--studio-ring`). See the host-side [worked examples](../../src/apps/Elsa.Studio.Web/Client/src/app/ui/README.md#host-side-worked-examples). |
| Field (today) | `exists` | `ui/forms/Field` (`StudioField`) wraps a control with its label and optional description/meta. This is a host-side pattern, not a stable export for independently bundled modules; see the UI README. |
| Button (additions) | `target, phase 3` | Pressed state (`:active` translate) and a spinner while pending. |
| Button (new variants) | `target, phase 4` | `outline` and `destructive` variants. Until they exist, use `default` / `ghost` and do not name `outline` or `destructive` in new call sites. |
| Tabs | `exists` | `ui/layout/Tabs` (`StudioTabs`). Sentence case; overflow collapse is `target, phase 1`. |
| DataGrid | `exists` | `ui/data-grid/DataGrid` (`StudioDataGrid`) supplies table semantics, row click, and Enter/Space activation. It does not isolate nested controls: callers must stop click and Enter/Space propagation for inner controls or use a guarded caller wrapper. Custom rows own their own target guards; Workflow Definitions guards nested controls itself. See the host-side worked examples in the UI README. |
| Secrets inventory | `exists` (#3069) | Search by secret name or display name is server-backed and requests up to 100 records. Counts identify rows shown and loaded alongside the result total; a submitted search appears in the count so draft text cannot change its meaning. More matches than loaded means the result is partial; a full 100-row page with a total of 100 is complete. Type, store, scope and status facets apply only to loaded rows, and scope choices describe loaded results. |
| DataGrid extras | `target, phase 4` | Sortable headers with an indicator; optional column picker persisted per grid; secondary actions in a ⋯ menu. |
| Status | `exists` | `StatusChip` (filled) for run and definition status; `StatusPill` (outline) for low-emphasis metadata. Colours from the status tokens only. |
| Incident details | `exists` | A compact **Next step** block sits before technical evidence and stays attached to the selected incident and current run. Keep active blocking, active non-blocking and historical states distinct; expose automatic retry timing only when the exact journal supplies it. Preserve copy, exact activity/input navigation, dialog Escape and trigger-focus return. |
| Feedback | `exists` (primitives) / `target, phase 2` (rich pattern) | `EmptyState`, `SkeletonRows` and `StudioAlert` exist. The richer pattern — icon, title, one-line hint, optional action — is the phase 2 target. |
| Overlays | `target, phase 3` | Popovers and dialogs animate in (fade + 4px slide/scale, ≤ 150ms) and honour `prefers-reduced-motion`. |
| Icons | `exists` | lucide, 16px in controls, 20px in nodes and tiles. |
| Disclosure kit | `exists` | `ui/disclosure/*`: `AddOption`, `RevealToggle`, `AdvancedSection`, `TechnicalDetails`, `OverflowMenu`, `ExpressionToggle`, `PeerViewSwitch`. Shared `persistKey` / `forceOpen`. Activity catalog tiers are lowercase `essential`, `optional` and `setting`; tier controls presentation independently of `isRequired` and runtime validation. Only an exact optional tier that is not required goes under one “More options” section. Missing, null, unfamiliar and setting tiers, plus required optional inputs, stay directly visible. Configured HTTP values start open and may be collapsed with a set-count badge through `allowCollapseWhenSet`; Runs opts in only because applied criteria stay visible as chips outside the disclosure. Other callers retain the existing default. Host-reported errors, option-source failures and editors without complete local-error reporting keep the section open; explicit force-open remains authoritative, and a forced reveal does not overwrite the saved collapsed preference. Only reported blocking errors receive an Error badge. Motion ≤ 150ms via `--studio-disclosure-duration`, honours `prefers-reduced-motion`. Keyboard + `aria-expanded` / `aria-controls`. Fixture at `/dev/disclosure` in dev builds only (`import.meta.env.DEV`) and `tests/browser/disclosure.html`. Settings and other surfaces do not use the kit yet. |

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

`exists` (ADR 0085 D7). `UserMenu` sits on the topbar after the theme control.

- The trigger shows the signed-in display name (or the account id when there is none).
- The menu repeats the name under "Signed in as" and offers **Sign out**.
- Sign out calls `useAuthContext().logout()`, which posts to `/_elsa/identity/logout/{provider}`.
  The session it leaves is anonymous, so `RequireAuth` sends the browser to the provider's
  challenge: the backend-owned login page. Studio renders no login page of its own.
- A failed sign-out keeps the menu open and says so in place.
- When no auth provider is configured, the menu is omitted.

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
| Minimap | `exists` | Designer (`GraphAuthoringCanvas`) shows `MiniMap` only when the graph’s unzoomed bounds exceed the canvas pane. Run / executable canvases still render `MiniMap` unconditionally. |
| Fit / zoom | `exists` | xyflow `Controls` on the same canvases. First-visit and Auto layout `fitView` clamp `maxZoom` to 1 so small graphs stay at their natural size. |
| Empty-canvas add | `exists` | `.wf-empty-canvas-add`; editorial layout also has **Add step**. |
| Save-state indicator | `exists` | `.wf-editor-save-status` in the Designer header (pending spinner / saved check). |
| Duplicate / copy-paste | `exists` | In-workflow only. Ctrl/Cmd+C and Ctrl/Cmd+V copy the selected activities; Duplicate is on the node context menu and Ctrl/Cmd+D. Fresh ids and names, offset position, one undo step, nested containers remapped. Cross-workflow clipboard and graph-editor paste are later slices. |
| Node toolbar | `target, phase 3` | Hover/selection toolbar on the node (not a permanent chrome bar). |

## Home

`exists`: Home replaces Dashboard at `/`, `/dashboard` and `/overview`. Modules contribute
widgets with `registerHomeWidget` (id, title, size, lazy component, optional
`requiresFeature`). The layout is a 12-column widget grid. Signed-in users persist it at
`/_elsa/studio/home-layout`; admins persist the default at `/_elsa/studio/home-layout/default`.
Reset deletes the user layout. Anonymous/demo sessions use `localStorage` or the built-in
default.

The built-in default landing is Get started, Quick actions, Recent workflows, Recent
runs, and Needs attention. **Get started** ticks Create a workflow / Run it / See what
happened from real state, links to the Elsa 4 docs for an empty install, hides when every
step is done or dismissed, and can be restored from Add widget. **Quick actions** link to
create a workflow and open the designer. Recent runs show status. List widgets have
title / description / CTA empty states. **Tasks** is in the default layout and the widget
registry, and only renders when `user-tasks` is enabled (`Elsa.Studio.UserTasks` matches).
`requiresFeature: "workflows"` matches `Elsa.Studio.Workflows` only (not `Contoso.Workflows` or `Elsa.Studio.WorkflowsX`). A missing `enabledFeatures` list hides gated widgets.

`exists`: a durable **Workflow tasks** group sits before the configurable grid and is not a widget. **Create workflow** consumes a one-use request to open the existing Workflows create dialog; **Browse workflows** still opens the definition collection. **Continue editing** reads the complete authorized definition list and uses actual current `draftId` values: one candidate opens that exact definition, while multiple candidates show a chooser with names and stable IDs. Empty results, request failures and missing current drafts remain distinct; a failed or stale selected definition keeps its ID and offers retry/browse recovery without silently choosing another workflow or creating a draft. **Needs attention** links to Runs' active-incident view. When Workflows is not composed the task group is absent; known permission denials are explained at Home while the existing server endpoints remain authoritative. None of these entries alter saved widget order, options or Get started behavior.

**Edit home** is a tinted mode with add, remove, reorder, Add widget, Reset to default, and
(for `studio/preferences/defaults:manage`) Save as server default. Normal view collapses
per-widget chrome into `OverflowMenu`. Add widget moves focus into the dialog, closes on
Escape, and returns focus to the trigger. Reorder keeps focus on the moved widget and
announces the new position.

## Dashboard widget chrome

`exists`: normal Home view shows one ⋯ `OverflowMenu` per widget (Refresh, Options, Resize,
Remove). Edit home shows move, size, settings, refresh and hide in `.dashboard-widget-actions`.

`target, phase 1`: remaining chrome stays hidden until the widget is hovered or focused. Title,
description, pinned mark and body stay visible (content first).

## Activity icons

`exists`: one resolver, `resolveActivityIcon` in `activityIcons.ts`, used by the designer
palette, canvas nodes, and the run-view timeline. Order: module-supplied catalog `iconName`
(any lucide export or kebab name, `asset:<package>/<path>`, or `ext:<name>`), then the
historical `icon` kind token, then the activity type key, then the category icon, then
`executionType === trigger`, then a reusable/generic fallback. Definition IDs alone never
override a more specific icon: recommended CLR activities also have definition IDs, so a
semantic type or category icon takes precedence over the reusable glyph when catalog provenance
does not provide an explicit icon.
An `icon` / `iconName` that is not a known kind, not a
well-formed lucide / asset / extension name, or is unsafe is ignored so the later steps
still run. `renderActivityIcon` (in `activityIconView.tsx`) lazy-loads the lucide table from
`activityIconGlyphs.tsx` so the Workflows entry and landing paths stay inside the bundle
budget; a rejected chunk load or a throwing glyph keeps the placeholder. Unmapped lucide
names load on demand and show the category placeholder briefly while the import resolves;
`asset:` values render as an `<img>` (they cannot follow tone tokens) resolved against the
Studio host base path; `ext:` values resolve through the public `registerActivityIcons`
registry in `@elsa-workflows/studio-sdk` (optionally also on
`api.workflowDesigner.registerActivityIcons` — callers must guard when the host omits it),
matching names case-insensitively and re-rendering if the icon is registered after first paint.
A failed load falls back to the category / type icon and never takes down the palette or canvas.
CLR activities set the catalog value with `[Activity(Icon = …)]`. The facet is presentation
only and is excluded from the descriptor hash.
Palette rows and canvas nodes set `data-tone` from `resolveActivityIconTone` and colour
through `--wf-activity-*`, which honour the host override `--studio-wf-activity-*` and then
`--studio-success` / `--studio-accent` / `--studio-warning` / `--studio-danger`. Lucide and
registered React icons should use `currentColor` so they follow those tone tokens.

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

## Author/operator walkthrough

Use this short route to connect the existing patterns without turning them into a new framework:

1. **Author:** From Home, create or resume the exact workflow draft. Configure a service with the required fields visible and any configured HTTP options in the existing disclosure. Save, use Test Run, then review and publish that same draft content; keep transient Test Run, durable published version and execution status distinct.
2. **Operator:** From Needs attention, open the exact run and its activity evidence, choose only a supported next step, then return to the validated Runs query. The incident guide remains the source for evidence, permissions, confirmation, and retry timing.
3. **Resources:** Create a disposable secret with the value masked, disclose optional configured metadata, and search by name. State shown, loaded, and server-total counts separately; local facets describe loaded rows only. Preserve entered values on a real failure and explain denied or unavailable states without implying full inventory coverage.
4. **Feature settings:** Review the selected draft, cancel and verify it is unchanged, then apply a justified change only to the selected target. Keep the review/cancel/apply result bound to that draft and the observed visit; do not imply a broader persistence guarantee.

These examples do not certify a composed journey. The [#3075 acceptance record](https://github.com/elsa-workflows/elsa-foundation/issues/3075) owns the nine final rows and their source/build evidence; implementation-slice acceptance does not replace that qualification.

### UX-01–UX-18 implementation-slice map

These links locate the existing implementation records. They do not replace #3075's composed evidence or change the state of any issue.

| Slice | Existing issue record |
|---|---|
| UX-01 | [#3058](https://github.com/elsa-workflows/elsa-foundation/issues/3058) |
| UX-02 | [#3059](https://github.com/elsa-workflows/elsa-foundation/issues/3059) |
| UX-03 | [#3063](https://github.com/elsa-workflows/elsa-foundation/issues/3063) |
| UX-04 | [#3063](https://github.com/elsa-workflows/elsa-foundation/issues/3063) |
| UX-05 | [#3062](https://github.com/elsa-workflows/elsa-foundation/issues/3062) |
| UX-06 | [#3064](https://github.com/elsa-workflows/elsa-foundation/issues/3064) |
| UX-07 | [#3065](https://github.com/elsa-workflows/elsa-foundation/issues/3065) |
| UX-08 | [#3060](https://github.com/elsa-workflows/elsa-foundation/issues/3060) |
| UX-09 | [#3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066) |
| UX-10 | [#3070](https://github.com/elsa-workflows/elsa-foundation/issues/3070) |
| UX-11 | [#3067](https://github.com/elsa-workflows/elsa-foundation/issues/3067) |
| UX-12 | [#3073](https://github.com/elsa-workflows/elsa-foundation/issues/3073) |
| UX-13 | [#3068](https://github.com/elsa-workflows/elsa-foundation/issues/3068), [#3069](https://github.com/elsa-workflows/elsa-foundation/issues/3069) |
| UX-14 | [#3061](https://github.com/elsa-workflows/elsa-foundation/issues/3061) |
| UX-15 | [#3072](https://github.com/elsa-workflows/elsa-foundation/issues/3072) |
| UX-16 | [#3070](https://github.com/elsa-workflows/elsa-foundation/issues/3070), [#3071](https://github.com/elsa-workflows/elsa-foundation/issues/3071) |
| UX-17 | [#3074](https://github.com/elsa-workflows/elsa-foundation/issues/3074) |
| UX-18 | [#3073](https://github.com/elsa-workflows/elsa-foundation/issues/3073), [#3131](https://github.com/elsa-workflows/elsa-foundation/issues/3131), [#3132](https://github.com/elsa-workflows/elsa-foundation/issues/3132) |
