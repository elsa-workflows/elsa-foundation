# Quickstart: Connection Creation Disclosure

**Status**: Reproducible local gates. The current review-fix source passed the expanded Connections tests and corrected Connections management browser spec; see tasks.md for counts. Canonical type/lint passed on the revised source; root checks generated maps after documentation updates. Hosted exact-head CI remains incomplete.

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

Run the real Connections browser proof directly, avoiding package-script argument forwarding. The test must cover metadata-only stubs, narrow and desktop layout, keyboard disclosure behavior, accessible expanded state, placement switching, preserved values, optional address one-of guidance only when both address fields are optional, and required-address behavior without either/or copy:

    pnpm --dir Studio exec playwright test tests/browser/connections-management.spec.ts --workers=1

Run the architecture suite serially after restoring its CI project graph, then check generated maps. Root owns execution and map-refresh decisions. The base f7c417d map job failed on stale docs/maps/manifest.json and docs/reports/maps-v1-findings.md; root later refreshed and reviewed the narrow map output, and the recorded local map check passed. Root will rerun map generation/check after this documentation cleanup. Do not attribute the original baseline finding to this feature. Review any new generated findings and stage only changed generated paths, including docs/maps/manifest.json if it changes.

    bash tools/architecture/restore-ci-project-graph.sh
    dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
    dotnet run --project tools/maps/Elsa.Maps.Generator -- check

Do not run a whole-solution local build for this client change; rely on the affected package builds above and required CI. Local gate counts are snapshots, not hosted CI evidence. PR #3105 has no completed exact-head CI, merge, or resulting-main proof yet.
