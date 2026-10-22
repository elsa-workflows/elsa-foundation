# Validation quickstart: Studio list consistency

This is a future validation guide, not a record of executed tests. Implementation starts only after root approves the Draft package and confirms there is no failed required base gate or conflicting ownership. The pinned frozen source head is `58a36c93f1395a0479868af19754aac2043be818`; root may choose a newer head. Pending Home CI remains pending, not waived. Root integrates qualified resulting main before final PR qualification or merge.

## Prerequisites

- Work from the root-approved pinned head or newer source SHA, with the selected issue and claim still current. Do not block initial source work on resulting-main integration once root approves the pinned head; do not claim pending Home CI passed.
- Use the existing Studio workspace dependencies and normal source-built `Elsa.Workbench` plus `Studio.Web` composition. Do not add dependencies or switch to a fixture-only host for acceptance.
- Seed authorized workflow/activity definitions with multiple pages, different activity authorities and draft/published states, folder/unfiled workflows, repeated and long names, and long technical IDs. Include distinct collection origins and an expired/invalid cursor scenario where the existing server setup supports it.
- Keep fixture-only cases labeled as supplemental evidence.

## Focused behavior checks

1. Add and run the Activity return-context test first against the approved source base. Record the actual result for editor Back, browser Back, multiple origin entries, direct-entry fallback, and reload. This is the existing functional defect; do not claim a red result unless observed. Also exercise auth-session/base-URL changes through the existing local redaction seam and assert restored history cannot recommit previously displayed rows; this does not test or promise a global query-cache fix.
2. Next, add identity disclosure/copy acceptance tests. Missing controls on the old base are a new-feature gap, not evidence of an existing row-event bug. After implementation, temporarily remove the nested-row guard, run the test to prove it fails, restore the guard, and rerun green.
3. From `Studio/`, run the focused Workflows client tests:

   ```bash
   pnpm --filter @elsa-workflows/studio-workflows exec vitest run --maxWorkers=2 --minWorkers=1 \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/activityDefinitions.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitions.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionBrowseLocation.test.ts \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionSearch.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionBrowseBoundaries.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionRoute.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowDefinitionStatus.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowInstances.test.tsx \
     src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowRunHistory.test.tsx
   ```

   Include Activity Definitions, Workflow Definitions browse/status, and Runs identity/action coverage. Retain the actual Vitest counts and output.
4. Run supplemental browser journeys from `Studio/`:

   ```bash
   pnpm exec playwright test tests/browser/activity-definitions.spec.ts tests/browser/workflow-definition-browse-context.spec.ts --project=chromium --workers=2
   ```

   These fixtures do not replace normal-host acceptance.

## Normal-host acceptance

1. Build and serve the real source-built Workbench with the normal Studio Web shell and backing APIs. Record exact source SHA, build command/result, served JS/CSS asset identities, authorized data setup, and cleanup evidence.
2. Use a bounded representative matrix: inspect the three lists at 1280px/light and 1440px/dark with representative populated, empty, failed, denied/unavailable, and expired-snapshot states; at 390px verify long names/IDs, keyboard use, row actions, and selected Activity return/recovery states. Capture side-by-side screenshots for root review; do not attempt every state/theme/viewport combination.
3. With keyboard only, use search and filters, expand/collapse identity details, copy exact IDs, invoke row actions, open an Activity Definition, and return through editor Back and browser Back across two different collection entries. Confirm no nested control also opens its row.
4. Confirm workflow browsing, folder/lifecycle/selection/Move behavior, Activity authority/draft/published behavior, Runs permissions/confirmation, opaque cursor scope, and authorization redaction remain intact.
5. Remove seeded records and temporary host data/processes. Run the complete affected client suites, relevant builds/lint, applicable e2e, architecture and generated-map checks on the integrated candidate. Root owns final exact-head and resulting-main gates; record unavailable checks as pending rather than passed.
