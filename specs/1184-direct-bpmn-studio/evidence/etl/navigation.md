# Native → ETL navigation preparation

T023–024 are implemented at source `77bb8dd5c` (including SDK/native inspector source `ddad5c8cc` and the creation-hint correction `831d355fe`). This increment does not establish actual RunEtl publication/replay, live Studio navigation, host restart or ETL recovery. Those remain T021/T025–026. Earlier direct-human and frozen-native receipts retain their own sources.

## Implemented behavior

The optional `workflow.run.related-links` Slot uses the existing Studio registry and Host Policy. A contributor names its native activity types and exact public state-view kind/version. The common native activity inspector performs the authorized state-view read, verifies workflow/activity/node identity, and supplies only the matching public projection to the contributor. Workflows imports no ETL implementation and reads no private activity frames. The Slot is wired into ordinary run details, the inspector fallback and the designer's attached Test Run inspector. Missing contributors/capabilities leave native inspection useful. Foreign origins/backends, retired sessions and mismatched projections cannot produce an active link.

Live revisions coalesce behind the outstanding exact read. They do not repeatedly abort it; a reference that was initially unavailable can appear on a later revision without reselection. Backend/account/tenant/context/occurrence changes still retire the old read immediately.

ETL contributes `Open ETL run` only from its owned `etl.run-reference/1` public shape with a matching native result address. The URL retains the selected backend and exact ETL RunId. The existing ETL page reauthorizes that exact run directly, including when the catalog is empty, delayed or unavailable; it does not search pages to discover it. Native return navigation retains the selected occurrence. Selecting another ETL run updates the URL and drops the old native association. History/context changes retire old reads and cancellation work. Older hosts without the optional Slot retain their existing ETL/module behavior.

## Verification and limits

- 125 focused workflow cases pass, including 24 new related-link controls and the existing 85 activity-details/16 execution-browser controls.
- 38 SDK/registry/compiler-consumer cases pass. The canonical consumer verifies the new API remains optional and its projected data stays unknown to the host. The three existing ambient declaration consumers retain their old contracts.
- 50 affected ETL cases pass:25 navigation/reference controls,23 run-page controls and2 module-registration controls. The 13 pre-existing cases remain included.
- The declared SDK build (`tsc --noEmit` in `StudioSdk`) and the affected Workflows/ETL typechecks pass. Targeted ETL lint has zero errors/warnings. Targeted SDK/workflow lint has zero errors and one unchanged empty-interface warning in `ElsaStudioBackendContext`; it already exists in source831d355fe.

Before implementation, the native inspector did not render the projected link, the host had no corresponding registry, and the ETL URL did not reopen its run. Those controls failed as expected. A separate live-revision control exposed repeated cancellation; it passes after coalescing. Filtered before-runs leave other cases unselected; these are not quarantine or acceptance passes.

An exploratory raw `Hosting/Client/tsconfig.json` invocation failed with unresolved workspace aliases and test-fixture diagnostics. That package has no declared typecheck script; this probe is retained as failed, not counted as a passed gate or evidence of a healthy baseline. The correct SDK package's declared build and public compiler-consumer checks above pass. Two ETL fixture type errors were also corrected by supplying the complete binding-alias shape; the final ETL typecheck passes. No unrelated host test/source was changed to silence diagnostics.

These are React component/typed API tests with mocked HTTP. No physical ETL host, owned browser or compatibility matrix was run for this preparation increment. Package/browser/API/architecture/full regression, independent review, exact-head required CI and resulting-main checks remain delivery gates. The [machine-readable receipt](navigation-qualification.json) records the source, commands and outcome distinctions. Raw logs are retained for the completed ETL journey's evidence publication.

## Reproduce

From `Studio/src/essentials/Elsa.Studio.Workflows/Client`, use the installed runner:

```sh
node ../../../../node_modules/vitest/vitest.mjs run src/__tests__/workflowRunRelatedLinks.test.tsx src/__tests__/workflowActivityExecutionDetails.test.tsx src/__tests__/activityExecutionBrowser.test.tsx
```

From `Studio/src/essentials/Elsa.Studio.Hosting/Client`:

```sh
node ../../../../node_modules/vitest/vitest.mjs run src/__tests__/registry.test.ts src/__tests__/sdk-typing-copies.test.ts
```

From `Extensions/Etl/studio/Elsa.Studio.Etl/Client`:

```sh
node ../../../../../Studio/node_modules/vitest/vitest.mjs run src/__tests__/runsPage.test.tsx src/__tests__/module.test.tsx src/__tests__/etlRunNavigation.test.ts
```

From the repository root:

```sh
node Studio/node_modules/typescript/bin/tsc --noEmit -p Studio/src/essentials/Elsa.Studio.Hosting/StudioSdk/tsconfig.json
node Studio/node_modules/typescript/bin/tsc --noEmit -p Studio/src/essentials/Elsa.Studio.Workflows/Client/tsconfig.json
node Studio/node_modules/typescript/bin/tsc --noEmit -p Extensions/Etl/studio/Elsa.Studio.Etl/Client/tsconfig.json
```
