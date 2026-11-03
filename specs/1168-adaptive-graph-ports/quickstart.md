# Qualification

Run from `Studio/` after `pnpm install --frozen-lockfile`:

```sh
pnpm --filter @elsa-workflows/studio-workflows test
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
pnpm exec tsc --noEmit -p tests/browser/tsconfig.json
pnpm exec playwright test adaptive-graph-ports.spec.ts activity-duplicate.spec.ts editor-run-canvas.spec.ts intrinsic-labels.spec.ts theme-layouts.spec.ts activity-definition-graph-authoring.spec.ts
```

Local browser qualification used an owned Vite server on port 4187 with the same browser fixture/configuration, one Chromium worker and no reused server from another checkout. All 43 cases pass. These are real production-component interactions with fixture catalog/runtime data and simulated persistence, not a live backend or provider claim. Browser cases include both authoring hosts in light/dark, movement/history/reload, native draw/reconnect, named branches, 30 connected outcomes with no overlapping handles/labels, initial measured geometry, selected top labels at three zooms, both BPMN hosts and read-only Run rendering. Existing Sequence, occurrence-inspection, intrinsic-label and theme-layout cases also pass.

The complete Workflows suite has 2,692 tests across 199 files. Compare execution with `pnpm exec vitest list --json` in its Client directory. Production build passes unchanged budgets: entry 107.30 kB, CSS 169.73 kB, Definitions 384.21/384.50 kB and Activity upgrades 375.36/376.00 kB. Workflows/browser typechecks, changed-file ESLint, module Stylelint and diff whitespace checks pass. Architecture graph restore and complete architecture suite pass 592/592; generated-map solution filters are current. No project reference changed.

## Behavioral controls and review

- Restoring the original `graph.tsx` renderer makes the stacked-node browser regression fail: actual right, expected bottom. Restoring the implementation passes.
- Independent review round 1 at `3bc39850f` found top-label/toolbar overlap and fallback layouts incorrectly starting hysteresis before measurement. The selected-top-label browser test fails on that head; the staged-measurement store test fails on that head with right instead of bottom. Both pass after repair. The toolbar chooses a free side around top-facing outcomes, reserving zoom-scaled label clearance when all sides are occupied; movement history now starts separately for handles with usable connected geometry.
- The first full frontend run exposed jsdom's missing DOMMatrix when internals were refreshed before measurement. The implementation now waits for actual measurement; subsequent complete runs have no runner errors. Early browser failures were fixture-only capability/layout-envelope/reload assumptions and test-only runtime edge ID/read-only assumptions; none is recorded as a pass.
- Round 2 found dense top/bottom ports overlapping: the new 30-outcome regression fails at about 8px spacing before repair. Horizontal capacity now reserves at least 48px per port and moves overflowing outcomes to the next closest available side in catalog order. The geometry and rendered dense-outcome cases pass after repair.
- Subsequent review and exact-head CI/merge outcomes are recorded on [PR #3474](https://github.com/elsa-workflows/elsa-foundation/pull/3474). Queued, skipped and failed jobs are not passing coverage. Resulting-main verification remains a delivery gate.

## Inspected visuals

[Vertical light](evidence/vertical-light.png), [vertical dark](evidence/vertical-dark.png), [independent named branches](evidence/branches.png), [top outcome and selected-node tools after zoom](evidence/top-outcome-tools.png).
