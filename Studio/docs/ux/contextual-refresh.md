# Contextual refresh

## Acceptance

- **Goal:** Refresh the active collection without losing filters, pagination, selection or work in progress. Optionally keep an operational list current at a chosen interval.
- **Reference:** Workflow Definitions and the shared `StudioPageHeader` action area; [module UI guidance](../module-ui.md).
- **States:** No Contribution (no control), manual default, immediate refresh, loading/disabled, automatic interval selected, hidden browser tab, inactive workspace tab, open mutation dialog, empty/populated lists, request failure/retry, access denied, backend/view change, narrow layout, light/dark modes.
- **Coverage:** Shared-control timer and keyboard tests; workflow collection integration tests for retained state and pending refresh; browser checks for rendered placement, selectable intervals, focus return and narrow layouts.
- **Excluded:** Editors, inspectors and unrelated collections do not opt in. Existing Run observation and Dashboard refresh orchestration are separate capabilities and are unchanged.

## Ownership

The shared page header owns the contextual refresh Slot, `studio.page-header.refresh`.
Views opt in through a `StudioRefreshContribution` on `StudioPageHeader.refresh`.
The header owns placement and the shared control owns scheduling; the view owns data,
authorization, request state and local state. No global reload or cross-view refresh is performed.

Manual is the default. Auto-refresh is explicitly supported by the contributing view and
offers 15 seconds, 30 seconds, 1 minute or 5 minutes. The choice is local to the mounted
view, not persisted across navigation or backend changes. Hidden/inactive views and
disabled/loading Contributions do not poll, and requests cannot overlap. Existing
collection results remain visible during a background refresh; access denial removes them.

See [the module-author contract](../module-ui.md#contextual-refresh) to opt in.

## Local verification

- Passed: 62 workflow collection tests, 21 shared-control tests, the opt-in page-header test, and five Chromium fixture checks covering keyboard selection/focus, polling, view-local visibility, retained tables, 390px/1440px and light/dark modes.
- Passed: workspace production build; changed-file ESLint (two existing unused-symbol warnings in `WorkflowInstances.tsx`); full CSS and module-layout lint; browser, live and acceptance TypeScript checks. Hosting and Workflows typechecks completed successfully within the aggregate attempts.
- Not green: aggregate `pnpm typecheck` timed out twice under heavy shared-machine load. Full `pnpm lint` fails on existing generated artifacts and unrelated source errors. The host accessibility suite has an independently reproduced failure in DataGrid's interactive-ancestor row click test, outside the refresh path.
- Visual fixture captures: `artifacts/contextual-refresh-browser/`. These are supporting local evidence, not current-commit composed-host acceptance. The composed-host journey and downloadable committed-revision evidence remain unverified; the acceptance runner requires a clean committed checkout.
- Independent fixture review: a dark-theme contrast concern was traced to screenshots taken mid-theme-transition. Captures now wait for stable animations, assert that opening the menu preserves the enabled main button's color/opacity, and pass in both themes and widths. The reviewer confirmed the concern resolved in the regenerated dark desktop captures; this is not composed-host acceptance.