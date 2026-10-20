# Quickstart: Stable workflow search

This is the repeatable implementation verification sequence. Executed, source-qualified results are recorded in [PR #3118](https://github.com/elsa-workflows/elsa-foundation/pull/3118); listing a command here does not claim a pass. The browser fixture composes the real `WorkflowManagementPage` and exercises editor replacement and Back behavior.

## Focused Workflows Client checks

Run from `Studio/src/essentials/Elsa.Studio.Workflows/Client`:

```bash
pnpm exec vitest run
for seed in 1 11 29; do
  pnpm exec vitest run --sequence.shuffle --sequence.seed="$seed" || exit 1
done
pnpm typecheck
pnpm build
```

These use the package’s existing `test`, `typecheck`, and `build` scripts; `build` runs Vite and the existing bundle-size check. The focused route, search, status and browse-boundary tests cover session transitions and cursor preservation without a second test-only paging state. Before interacting with a current-result control, wait for the current response to render: request dispatch alone is insufficient while prior rows are deliberately display-only.

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
bash tools/architecture/restore-ci-project-graph.sh --locked-mode
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

The feature changes no backend behavior or dependency boundary. Under main PR #3116, `-- check` compares the tracked dependency map; documentation maps are generated locally or published to `generated-maps` and must not be staged. Review exact-head hosted checks and verify resulting-main CI/dependency-map checks after merge. See [current map policy](../../docs/contributing/generated-maps.md).

## RED/GREEN evidence to record

Baseline and before/after results are recorded in `tasks.md` and the PR receipts. Some original generation/empty-state guards already passed before implementation; they are preservation evidence, not newly reproduced defects. The retained-results and Back-restoration failures were observed before their implementation. Preserve the original assertions when correcting asynchronous test readiness, and retain failed logs separately from later passing runs. None of this is a production latency guarantee.
