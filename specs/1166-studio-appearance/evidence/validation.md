# Validation receipt

The implementation was reconciled with main's authoring UX change #3427 (`4c79e3688`). No backend runtime or theme-store schema changed.

- Host: 598/598 tests across 53 files; discovery lists 598.
- Workflows: 2,673/2,673 tests across 197 files after reconciliation; discovery lists 2,673.
- Browser: 59/59 tests covering the new style matrix, existing layouts, theme-store selection and shell scroll containment, plus the independent reload regression (1/1). The matrix uses actual shell, authoring, runtime inspection and list components with the repository's isolated HTTP fixtures. It is UI/state evidence, not a real backend execution qualification.
- Workspace typecheck and full lint pass (56 existing ESLint warnings, zero errors); both host production bundles and the Workflows production build pass.
- Workflows budgets remain unchanged: primary JavaScript 107.30/127.50 kB, CSS 168.91/185 kB, authenticated Definitions 383.39/384.50 kB, activity upgrades 374.54/376 kB, largest chunk 357.09/500 kB. Panel-only CSS was moved into its deferred owning components.
- Foundation architecture: 592/592 tests, zero skips; discovery lists 583 entries because two non-serializable MemberData theories expand from two entries to eleven cases at execution (five domain features and six collectible metadata factories). Generated solution-filter check passes. Architecture restore used the shared build-slot wrapper.

Commands ran from `Studio/` unless stated otherwise:

```sh
pnpm install --frozen-lockfile --offline
# In each affected host/workflow Client directory:
pnpm exec vitest run --maxWorkers=2 --minWorkers=1
pnpm exec vitest list --json
# From Studio/:
pnpm typecheck
pnpm lint
pnpm --filter @elsa-workflows/studio-web build
pnpm --filter @elsa-workflows/studio-workflows build
pnpm test:browser tests/browser/workspace-appearance.spec.ts tests/browser/theme-layouts.spec.ts tests/browser/theme-source-modes.spec.ts tests/browser/studio-scroll-containment.spec.ts --workers=2
# From the repository root:
bash tools/architecture/restore-ci-project-graph.sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --nologo
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-build --no-restore --list-tests --nologo
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Eight new mounted appearance regressions failed before implementation and passed afterward. A deliberate source mutation subsequently removed the hidden-filter reset, short-menu scrolling and graph focus outline; all three rendered regressions failed, then passed with the implementation restored. No retries, quarantines or skips were introduced. Initial locator failures were corrected to the actual button/radio roles and the display-name label that includes its character counter.

Independent PR review round 1 found two further defects: neutral button treatment replaced semantic confirmation fills, and a legacy preset-specific publication correction did not follow an independently selected palette. Both gained rendered regressions in all three styles. The correction preserves semantic fills and uses one contrast-safe publication text mix across palettes, removing the palette-specific component selector. Contrast is checked after the dialog's entry animations finish. Existing settings and menu keyboard tests now follow the explicit preset disclosure and independent palette semantics. Final aggregate counts and review status are on the PR.

An additional ad hoc host-wide `tsc` probe fails on existing SDK resolution, asset/env declarations, agent diagnostics types, component barrel default export and React ref types. Those offending declarations are unchanged from main. The supported workspace typecheck and host Vite builds pass; the ad hoc probe is not reported as green.

Rendered screenshots use the same Drift palette to make workspace differences comparable: [Quiet Studio](quiet-studio.jpg), [Focus Canvas](focus-canvas.jpg), [Open Studio](open-studio.jpg), and the [light list](open-list-light.jpg). The developer's running Studio host was not restarted. Exact PR revision, independent-review results and hosted CI state are recorded on the PR.
