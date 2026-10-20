# Quickstart: Connection Creation Disclosure

**Status**: Planned commands for implementation review; none were run during specification preparation.

Run package-level checks from the Studio workspace. These are the focused regression command and the affected package suites/builds:

    cd Studio && pnpm --filter @elsa-workflows/studio-connections exec vitest run src/__tests__/connectionCreateDialog.test.tsx
    cd Studio && pnpm --filter @elsa-workflows/studio-connections test
    cd Studio && pnpm --filter @elsa-workflows/studio-connections build
    cd Studio && pnpm --filter @elsa-workflows/studio-web exec vitest run src/app/ui/disclosure/__tests__/disclosure.test.tsx
    cd Studio && pnpm --filter @elsa-workflows/studio-web test
    cd Studio && pnpm --filter @elsa-workflows/studio-web build
    cd Studio && pnpm --filter @elsa-workflows/studio-ui build
    cd Studio && pnpm --filter @elsa-workflows/studio-workflows test
    cd Studio && pnpm --filter @elsa-workflows/studio-workflows build

Run the canonical Studio type, lint, CSS, and module-layout gates:

    cd Studio && pnpm typecheck
    cd Studio && pnpm lint

Run the real Connections browser proof directly, avoiding package-script argument forwarding. The test must cover metadata-only stubs, narrow and desktop layout, keyboard disclosure behavior, accessible expanded state, placement switching, preserved values, and the address restriction:

    pnpm --dir Studio exec playwright test tests/browser/connections-management.spec.ts --workers=1

Run the architecture suite serially after restoring its CI project graph, then check generated maps. Root owns execution and map-refresh decisions after reviewing the completed spec/tasks. The map job for base f7c417d was already verified failed before this feature because docs/maps/manifest.json and docs/reports/maps-v1-findings.md were stale; do not report the baseline as fresh or attribute that finding to this feature. If architecture or map checks identify an additional real change, record it and use the narrowest applicable repository procedure; review generated findings and stage only changed generated paths, including docs/maps/manifest.json when it changes.

    bash tools/architecture/restore-ci-project-graph.sh
    dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
    dotnet run --project tools/maps/Elsa.Maps.Generator -- check

Do not run a whole-solution local build for this client change; rely on the affected package builds above and required CI. No command result is claimed until the implementation work actually runs it.
