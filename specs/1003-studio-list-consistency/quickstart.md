# Validation quickstart: Studio list consistency

This is a validation guide, not a record of executed tests. Root approved the package before implementation; Home #3063 is accepted and qualified main is integrated. Bind each actual run to the selected clean candidate, its source-built artifacts, and its recorded result. The [issue](https://github.com/elsa-workflows/elsa-foundation/issues/3070) and [PR](https://github.com/elsa-workflows/elsa-foundation/pull/3258) remain the live delivery evidence record.

## Prerequisites

- Work from the clean root-selected candidate with the selected issue and claim still current. Preserve the accepted Home changes and integrated main; never relabel a prior head's evidence as current.
- Use the existing Studio workspace dependencies and normal source-built `Elsa.Workbench` plus `Studio.Web` composition. Do not add dependencies or switch to a fixture-only host for acceptance.
- Seed authorized workflow/activity definitions with multiple pages, different activity authorities and draft/published states, repeated and long names, and long technical IDs. The normal Workbench does not advertise `workflow-folders`, `workflow-definitions-page`, or `workflow-definition-folder-move`; do not invent folder seeding or advertise positive Move proof. Verify their controls stay hidden, bookmarked folder/Unfiled URLs show truthful unavailable-scope fallback, and the real effective-all read uses no unsupported filters or guessed endpoints. Retain supported-host unit/browser folder regressions as supplemental coverage; positive normal folder/Move cases are Not run — unsupported by this composition ([refinement](https://github.com/elsa-workflows/elsa-foundation/issues/3070#issuecomment-6379713156)). Include distinct collection origins and an expired/invalid cursor scenario where the existing server setup supports it.
- Keep fixture-only cases labeled as supplemental evidence.

## Focused behavior checks

1. Add and run the Activity return-context test first against the approved source base. Record the actual result for editor Back, browser Back, multiple origin entries, direct-entry fallback, and reload. This is the existing functional defect; do not claim a red result unless observed. Also exercise auth-session/base-URL changes through the existing local redaction seam and assert restored history cannot recommit previously displayed rows; this does not test or promise a global query-cache fix.
2. Next, add identity disclosure/copy acceptance tests. Missing controls on the old base are a new-feature gap, not evidence of an existing row-event bug. After implementation, temporarily remove the nested-row guard, run the test to prove it fails, restore the guard, and rerun green.
3. From `Studio/`, run the focused Workflows client tests:

   ```bash
   pnpm --filter @elsa-workflows/studio-workflows exec vitest run --maxWorkers=2 --minWorkers=1 \
     src/__tests__/activityDefinitions.test.tsx \
     src/__tests__/workflowDefinitions.test.tsx \
     src/__tests__/workflowDefinitionBrowseLocation.test.ts \
     src/__tests__/workflowDefinitionSearch.test.tsx \
     src/__tests__/workflowDefinitionBrowseBoundaries.test.tsx \
     src/__tests__/workflowDefinitionRoute.test.tsx \
     src/__tests__/workflowDefinitionStatus.test.tsx \
     src/__tests__/workflowInstances.test.tsx \
     src/__tests__/workflowRunHistory.test.tsx
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
4. Confirm workflow browsing, lifecycle/selection and capability-gated folder/Move behavior (using the composition boundary above), Activity authority/draft/published behavior, Runs permissions/confirmation, opaque cursor scope, and authorization redaction remain intact.
5. Remove seeded records and temporary host data/processes. Run the complete affected client suites, relevant builds/lint, applicable e2e, architecture and generated-map checks on the integrated candidate. Root owns final exact-head and resulting-main gates; record unavailable checks as pending rather than passed.
