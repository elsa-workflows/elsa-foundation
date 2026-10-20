# Validation guide — Studio secret-creation focus

Commands without a recorded result below remain planned and **UNRUN**. This guide distinguishes implementation baselines from final delivery checks.

## Recorded pre-change baseline — 2026-10-20

On base `d628fc0da1e44ce5ea61768fbf6377b43919bacb`, before any Secrets production edit:

- The untouched Secrets unit suite passed all 44 tests in seven files.
- Both new actual-host-style 1280×800 browser cases failed the intended geometry assertion: the full Value and preselected Configuration-key controls ended at y=774, below the visible dialog-body bottom y=698. Both measurements confirmed body scrollTop=0; no field interaction or scrolling preceded measurement. This is expected RED evidence, not a passing gate.
- The new direct Secrets TypeScript diagnostic failed. The module has no existing `typecheck` script, so canonical recursive workspace typecheck skips it. Source inspection found an obsolete local ambient `studio-sdk.d.ts` shadowing real SDK exports, plus four independently invalid generic API test doubles. After root approves all regression baselines, remove that duplicate declaration and accurately type only those mocks before running the direct check again. Do not modify the SDK or suppress diagnostics. The initial post-removal check exposed the partial module-registration fixture and generic-return erasure in Vitest mock wrappers; those fixtures were corrected with real SDK constructors and plain generic HTTP doubles, without broad API assertions or diagnostic suppression.

Root approved the pre-implementation baseline gate after reviewing the regression tests and an independent review of the browser assertions. The expanded unit suite has 48 passes and five expected failures because More options is absent. An initial test-fixture label typo (`text` versus `Text`) was corrected and the full suite rerun; it was not a source defect. The new browser suite has 11 expected geometry/disclosure failures and three preservation passes; the original nine browser cases passed before disclosure assertions were added. A subsequent targeted run reconfirmed Configuration metadata-only creation and both Edit cases (three passes), including the explicit unexpected-read counter on Configuration creation. These RED results authorize implementation, not delivery acceptance.

The pre-layout dependency gate subsequently passed: frozen-lock installation (818 supply-chain policy entries), Studio UI TypeScript build, direct Secrets typecheck, and Secrets Vite build. The new Secrets `typecheck` script makes this coverage part of the existing recursive workspace/CI command. Its full unit suite still has the same 48 passes and five expected disclosure failures. Root approved T010 and the required-field reorder only; disclosure implementation and delivery verification remain pending. See `3066-dependency-install.log`, `3066-ui-build-before-layout.log`, `3066-typecheck-before-layout-corrected.log`, `3066-build-before-layout.log`, and `3066-dependency-unit-baseline.log`.

Raw receipts are retained in the external execution record: `3066-unit-baseline.log`, `3066-required-source-baseline-red.log`, and `3066-typecheck-baseline.log`, `3066-unit-regression-baseline-corrected.log`, `3066-disclosure-browser-red.log`, and `3066-browser-preservation-baseline.log`. These baselines do not establish the final layout, production integration, or hosted gate results.

## Before production changes

1. Work from `agent/codex/3066-secret-creation` and confirm the implementation source still matches the approved spec's boundary.
2. From `Studio/`, run the current Secrets unit suite as a baseline:

   ```bash
   pnpm --filter @elsa-workflows/studio-secrets test
   ```

3. Add the actual-host-styles browser assertion and run it RED before editing production UI. At 1280×800, open the ordinary value-backed Create path and measure the full Value control rectangle against the visible client rectangle of the dialog content. Assert before any scrolling, with scroll position unchanged; do not use `scrollIntoViewIfNeeded` to qualify initial visibility. Repeat for a Configuration Store selected before the dialog opens.

   From `Studio/`:

   ```bash
   pnpm exec playwright test tests/browser/secret-scopes.spec.ts --grep "required source is visible at 1280x800" --workers=1
   ```

   Capture baseline geometry. Screenshot evidence may supplement the measurement, but does not replace it.

4. Add focused unit regressions for behavior at risk and run them before the production edit. From `Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/`:

   ```bash
   pnpm exec vitest run --config ./vite.config.ts ./src/__tests__/secretDialogs.test.tsx
   ```

   The new layout assertion should fail against the old order; tests for existing request and retention behavior should establish the baseline. No production edit precedes these RED/baseline checks.

## After implementation

From `Studio/`, run the affected package, browser, and workspace checks:

```bash
pnpm --filter @elsa-workflows/studio-secrets test
pnpm --filter @elsa-workflows/studio-secrets build
pnpm --filter @elsa-workflows/studio-secrets exec tsc --noEmit -p tsconfig.json
pnpm exec playwright test tests/browser/secret-scopes.spec.ts --workers=1
pnpm typecheck
pnpm lint
```

The new direct Studio UI dependency also receives its package build check:

```bash
pnpm --filter @elsa-workflows/studio-ui build
```

From the repository root, run the existing architecture guard and map check:

```bash
bash tools/architecture/restore-ci-project-graph.sh --locked-mode
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

The map `-- check` compares the committed dependency map. Documentation maps, manifest, and findings are published by CI on `generated-maps` after a push to `main`; they are not committed or staged. Do not run map generation for this UI-only change unless the root owner separately identifies a relevant need.

## Behavior evidence on the final source head

The browser test should prove actual host styles and dialog-body geometry at 1280×800, including the ordinary Value and preselected Configuration-key paths, without scrolling before measurement. Retain responsive coverage and check More options starts collapsed for the ordinary default-Store path. The full affected suite should also preserve:

- Authored optional values and exact custom Scope through manual collapse and reopen.
- The default Store excluded from the generic authored count; no sensitive field contents in count, summary, or fixture metadata.
- Existing Type/Store switching, empty-Store recovery, source retention, Value remasking on control recreation, and create payload semantics.
- Advisory Scope failure and Retry recovery without clearing focused/custom Scope or changing eligibility.
- Save error retention and focus, Cancel/Escape, focus return to the opener, and fixed actions while the dialog body scrolls.

Update the tall-error scenario to scroll to an actually below-fold optional field or long help after Expires becomes primary. Keep its body-scroll, fixed-action, error, and focus assertions. Use Playwright's semantic readiness and measured results; do not add arbitrary sleeps or weaken assertions.

Root records visual and keyboard review on the final implementation head. Fixture tests do not establish live Workbench, backend, permission-service, or screen-reader results. Run configured hosted required checks and independent review on the exact final diff; after merge verify main's CI and maps gates.

## Spec lifecycle at implementation completion

In the implementation PR, before merge, set the spec status to `Implemented` and remove `checklists/requirements.md` plus its entry from the plan's feature tree. This checklist covers specification quality only and is not delivery evidence. The status map is published by the generated-maps CI flow after merge; do not commit or force-add ignored documentation-map files.
