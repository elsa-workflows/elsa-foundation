# Implementation Plan: ParallelForEach runs one body pass per item at the same time

**Branch**: `agent/claude/3052-spec1243-parallel-foreach` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

## Summary

Add a `ParallelForEach` loop to the control-flow activities. It reuses `ForEach`'s collection resolution and
per-pass iteration frames, schedules every pass at start, and completes when every pass has completed, counting passes
by iteration id from the durable store. Like `Sequence`, it waits for a faulted pass and accepts its retry. The shared
loop structure handler reports this loop's body as unordered with itself, so spec 266's concurrent-write warning
applies. The Elsa 3 converter then maps `Elsa.ParallelForEach` onto it, rewrites the per-pass `CurrentValue` and
`CurrentIndex` reads, and reports the behaviours Elsa 4 does not keep. Studio offers and inspects the loop.

## Technical Context

**Language/Version**: C# / .NET (repo default); TypeScript for Studio
**Primary Dependencies**: existing runtime scheduler, iteration-frame primitive (ADR 0028), value-flow validation
**Testing**: xunit only (no FluentAssertions); converter corpus tests; one e2e run of the JTest `single-outcome` suite
**Project Type**: modular framework (Design/Runtime split)
**Constraints**: Runtime never references Design (Elsa §E2.2); no new extension-point kind; no author-facing
concurrency setting (FR-009)

## Constitution Check

- Sanctioned patterns only: a new activity in an existing module, and one more `LoopKind` handled by the existing
  `LoopStructureHandler`. No new contract, event or replacement point.
- Extension-point catalog: the control-flow `EXTENSION_POINTS.md` lists the new activity and its child slot.
- Feature registration test: `ActivitiesControlFlowFeature` registers the new structure handler (unit test, as for the
  other loops).
- No ADR change (research R-7). Join semantics stay with #4193.

## Design

### Runtime activity (US1, US2; research R-1, R-2, R-5)

- New `ControlFlow/ParallelForEach/Activities/ParallelForEach.cs`, with the same attributes as `ForEach` (structure
  kind `elsa.parallelforeach.structure` 1.0.0, child slot `ParallelForEach.Body`, `RefusesSecretBinding` on
  `Collection`, `ReplaySafe`), implementing `IRuntimeStructuralActivity`, `IRuntimeActivityChildCompletionHandler` and
  `IRuntimeAcceptsRescheduledFaultedChild`. It does **not** implement `IRuntimeActivityChildFaultHandler`.
- Extract `ForEach`'s `ForEachCollection`, `ToItemEnvelope` and `ToEnvelope` into a shared internal helper in
  `ControlFlow/Loops/` (for example `LoopCollection`), so both loops resolve, refuse and envelope items identically
  (FR-005). Messages keep the `ForEach` wording, with the activity name as a parameter.
- `ExecuteStructureAsync`: resolve the collection once. If it is empty or there is no body, return `Complete()`.
  Otherwise schedule every pass in item order, each with
  `StructuredExecutionIdentity.Iteration(parentExecutionId, index)` and a `LoopIterationScopeRequest`, then return
  `Defer`. Give `StructuredExecutionIdentity` a neutral iteration prefix, or a loop-kind parameter, so pass ids do not
  read `foreach-iteration`. Keep `ForEach`'s existing ids unchanged.
- `OnChildCompletedAsync`: read this loop's direct children with `IActivityExecutionStateStore.ListAllByParentAsync`,
  keep the body node's rows, group them by `IterationId`, and count an iteration as done when any of its rows is
  `Completed` or its latest row is `Cancelled` (a retried pass's successor carries the same iteration id). Verify
  which evaluation the runtime gives a parent when one child is cancelled by an alteration. If it gives none, the
  alteration path must queue a parent evaluation, or a cancelled pass would leave the loop waiting forever. Complete when done equals the item count
  (`Collection` length, read with O(1) on a JSON array), otherwise defer. The Break outcome needs no special case
  (FR-007).
- Confirm in a test that a rescheduled faulted pass keeps its `IterationId` and iteration frame. If rescheduling does
  not carry them today, the fix belongs in the reschedule alteration handler, for every loop, and is part of this
  work.

### Publish checks (US3)

- `LoopKind` gains `ParallelForEach` (iteration names `currentItem` and `currentIndex`, index switch `ExposeIndex`) and
  a flag `PassesUnordered`.
- `LoopStructureHandler` reports the body as unordered with itself when `PassesUnordered` is set (today it is always
  `FirstBeforeSecond`, lines 38-39), so `UnorderedVariableWriteAnalyzer` (lines 270-277) raises the spec 266 warning.
  Review `AssessChildResult` (lines 34-35) for the same assumption.
- Register `new LoopStructureHandler(LoopKind.ParallelForEach)` in `ActivitiesControlFlowFeature`.
- Spec 1222 completion and spec 1231 refusals follow from `LoopKind.IterationVariableNames`. Add tests to prove it.

### Converter (US4)

- `Elsa3BuiltInActivityMap.Entries.cs`: replace the `NoCounterpart("ParallelForEach", …)` entry with
  `Catalog("ParallelForEach", ParallelForEach, [Carry("Items", "Collection"), Slot("Body", "ParallelForEach.Body")])`,
  plus the no-body warning (FR-016) and the literal-value refusal (FR-017) as member rules.
- Per-pass names: register the new loop in `Elsa3LoopVariableReferencesRewrite`/`AncestorScopeNames` with the Elsa 3
  names `CurrentValue` → `currentItem` and `CurrentIndex` → `currentIndex`, so a resolved
  `getVariable("CurrentValue")` in a body pins to the loop node, nearest scope first (FR-013). A computed name keeps the
  run-time lookup. Check whether that lookup sees the Elsa 3 names. If it cannot be made to, the computed form is
  refused with guidance.
- Writes to those names: refused in the expression rewriter's setter path with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`
  (FR-014).
- `Elsa3ActivityNormalizer.ApplyEntryWarnings`: the Break warning's ancestor test also accepts the `ParallelForEach`
  type key (FR-015).
- New behaviour code for FR-016, for example `ELS3-BEHAVIOUR-PARALLEL-FOREACH-NO-BODY`, in
  `Elsa3ActivityDiagnosticCodes` with its message, and in the spec 236 diagnostic-codes contract.
- Remove `"Elsa.ParallelForEach"` from any list that treats it as unsupported. Keep the value-flow lowerer's message
  for its item reads (`Elsa3ValueFlowLowerer.cs:1663`).

### Studio (US5)

- `blockPromotion.ts`: add the activity type to the safe activities and `elsa.parallelforeach.structure` to the loop
  kinds.
- Toolbox, body-slot editing and variable completion come from the activity descriptor and structure kind. Verify them
  in the composed host per Studio UX acceptance, with screenshots for the PR.
- Instance view: passes show under the loop by iteration id, as `ForEach` passes do, and the retry action on a faulted
  pass uses the existing reschedule alteration.

### Docs and evidence

- `ControlFlow/ParallelForEach/README.md` (FR-020), the control-flow `EXTENSION_POINTS.md`, and the module README's
  activity list.
- Baselines: `essentials/tests/Activities/Design/Tests/Contracts/activity-contract-surface.baseline.json` and
  `docs/reports/evidence/activity-contract-parity/elsa4-surface.json`.
- JTest: rerun `single-outcome` on Elsa 4 and record the result in the S-15 ledger (`suites.md`, `dispositions.md`), on
  the S-15 branch or its successor.
- Close the #1152 row and #5832 in the implementing PR.

## Project Structure

```text
essentials/src/Activities/ControlFlow/
  ParallelForEach/Activities/ParallelForEach.cs          new
  ParallelForEach/README.md, EXTENSION_POINTS.md         new
  Loops/LoopKind.cs, LoopStructureHandler.cs             changed
  Loops/LoopCollection.cs                                new (extracted from ForEach)
  ForEach/Activities/ForEach.cs                          changed (uses LoopCollection)
  Results/StructuredExecutionIdentity.cs                 changed (iteration prefix)
  ActivitiesControlFlowFeature.cs                        changed
essentials/tests/Activities/ControlFlow/Tests/ParallelForEach/   new
extensions/Elsa3/src/Mapping/...                         changed (map entry, rewrite, normalizer, codes)
extensions/Elsa3/tests/Mapping/Tests/                    new cases
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/blockPromotion.ts   changed
```

## Complexity Tracking

None. The only new shared code is the collection helper extracted from `ForEach`, so that the two loops cannot drift
apart.
