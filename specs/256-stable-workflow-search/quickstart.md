# Quickstart: Stable workflow search

This is the proposed implementation verification sequence. Commands have **not** been run for this feature. The browser return-context scenario requires the planned fixture to compose the real `WorkflowManagementPage`; the current direct-list fixture cannot verify editor replacement and Back behavior.

## Focused Workflows Client checks

Run from `Studio/src/essentials/Elsa.Studio.Workflows/Client`:

```bash
pnpm exec vitest run
pnpm typecheck
pnpm build
```

These use the package’s existing `test`, `typecheck`, and `build` scripts; `build` runs Vite and the existing bundle-size check. The added focused session tests should cover route-owner transitions and cursor preservation without creating a second test-only paging state.

## Studio browser and integration checks

Run from `Studio`:

```bash
pnpm exec playwright test tests/browser/workflow-definition-browse-context.spec.ts --workers=1
pnpm typecheck
pnpm lint
pnpm build
```

The browser scenario should run against the default Studio Playwright configuration and exercise the real workflow-management route, later-page cursor, editor open, editor Back, browser Back, and full-remount default. Its fixture must record requested search/page/cursor behavior so the test can distinguish restored page state from a page-one replay. The existing configuration and browser setup may require the repository’s normal local prerequisites.

## Repository architecture and generated-map checks

Run from the repository root:

```bash
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

No backend source or dependency boundary is expected to change; these remain the repository’s standing architecture/map checks. Review hosted `Build & test` and `Maps` results for the exact implementation commit and check fresh-main CI and maps after merge under the repository’s post-merge gate.

## RED/GREEN evidence to record

Before implementation, run the new behavioral tests against the baseline and record each result. The critical RED scenarios are immediate supersession after search entry but before the delayed request starts, retained/labelled previous rows across a search from a later page, the selected-page request using an observed cursor, the synthetic definition-only `popstate` before list unmount, route-owner invalidation while the editor is open, and editor/browser Back restoration through the real route. Then rerun the same tests after implementation and record the GREEN results. Existing generation guards and the distinct empty/error render branches may already pass their baseline cases; report that honestly rather than claiming they were newly fixed. All commands above remain unrun and are not evidence of a pass or a production latency guarantee.
