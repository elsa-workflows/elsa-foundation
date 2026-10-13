# Studio UX design spec

Status: draft for the Elsa 3 → Elsa 4 parity series. This spec is the yardstick each UX PR is
reviewed against. It builds on [design-tokens.md](../design-tokens.md) (the token contract) and
[ux-polish-plan.md](../ux-polish-plan.md) (the per-surface backlog). Where the plan and this spec
disagree, this spec wins for new work, and the plan should be updated in the same PR.

## Principles

1. **Content first.** Chrome (toolbars, docks, widget controls) is hidden until it is needed. A page
   opens on its content, not on a diagnostic panel.
2. **One primary action per view.** Exactly one filled button per page header; everything else is
   outline, ghost or in an overflow menu.
3. **Tokens only.** Colour, radius, shadow, spacing and type come from `--studio-*` tokens. This is
   already linted for colour. Radius, shadow and type follow the same rule by review.
4. **Every theme, every mode.** Each change is checked in Meridian Light and one dark theme, and
   must not break Dim or High contrast.
5. **Quiet typography.** Sentence case for labels, tabs and panel titles. Uppercase mono is
   reserved for page eyebrows and table headers.

## Layout

| Region | Rule |
|---|---|
| Shell | Sidebar or top menubar (theme or user choice). The top bar holds the page title, global actions, the theme control and the user menu. |
| Bottom dock | Collapsed by default; remembers the user's last state; a status dot signals new errors. |
| Page | A page header (eyebrow, title, one-line description, actions), then content directly on the page surface. No wrapping card around a whole page. |
| Lists | A single filter row (search + segmented status + "More filters"), then the grid, then the pager. Filters apply live. |
| Designer | Palette / canvas / inspector. Tab labels never truncate: they collapse to icon + tooltip below the width they need. |

## Components

| Component | Rule |
|---|---|
| Button | `ui/forms/Button` variants only: primary, outline, ghost, destructive; sizes sm/md. Pressed state, focus ring via `--studio-ring`, spinner while pending. |
| Tabs | `ui/layout/Tabs`. Sentence case, icon optional, overflow handled by the component. |
| DataGrid | Sortable headers with an indicator, optional column picker (persisted per grid), row click opens, secondary actions in a ⋯ menu. |
| Status | `StatusPill` for run and definition status; colours from the status tokens only. |
| Feedback | `FeedbackStates` for empty/loading/error: icon, title, one-line hint, optional action. |
| Overlays | Popovers and dialogs animate in (fade + 4px slide/scale, ≤ 150ms) and honour `prefers-reduced-motion`. |
| Icons | lucide, 16px in controls, 20px in nodes and tiles. Activities use their own icon, never a shared generic one. |

## Appearance

The appearance model (`app/themes/appearance.ts`) is the single source for user choices:

| Preference | Values | Default | Stored as |
|---|---|---|---|
| Theme | any selectable theme id | configured `Themes:DefaultThemeId`, else the first theme | `elsa-studio-theme` |
| Mode | `system`, `light`, `dark`, `dim`, `high-contrast` | `system` (follows OS colour scheme and contrast, live) | `elsa-studio-theme-mode` |
| Navigation | `theme`, `left`, `top` | `theme` | `elsa-studio-nav-mode` |

- The topbar control and a Settings › Appearance page both edit this model; neither keeps its own state.
- "Reset to defaults" clears all three keys.
- A theme that lacks the chosen mode renders its nearest supported mode, but the preference is kept.
- Theme definitions (Theme Builder, theme store) are admin-level and server-side; appearance
  preferences are per browser.

## Review checklist for UX PRs

- [ ] One surface; no unrelated refactors.
- [ ] Before/after screenshots at 1600×1000 @2x, Meridian Light and one dark theme.
- [ ] Only `--studio-*` tokens; `pnpm lint:css` passes.
- [ ] Keyboard and focus behaviour unchanged or better; axe test still passes.
- [ ] Checked against ux-polish-plan.md; plan updated if the item is now done.
