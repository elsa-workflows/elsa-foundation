# Implementation Plan: ParallelForEach runs one body pass per item at the same time

**Branch**: `agent/claude/3052-spec1243-parallel-foreach` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

## Summary

Add a `ParallelForEach` loop to the control-flow activities. It reuses `ForEach`'s collection resolution and
per-pass iteration frames. It schedules up to `MaxDegreeOfParallelism` passes at once (default 16, at most 64, cap
1,000 items, per the owner's large fan-out decision 1), starts the next item's pass as each one completes, and
completes when every pass has completed. Passes are counted by iteration id through a new store count query. Like `Sequence`, it waits for a faulted pass and accepts its retry. The shared
loop structure handler reports this loop's body as unordered with itself, so spec 266's concurrent-write warning
applies. The Elsa 3 converter then maps `Elsa.ParallelForEach` onto it, rewrites the per-pass `CurrentValue` and
`CurrentIndex` reads, and reports the behaviours Elsa 4 does not keep. Studio offers and inspects the loop.

## Technical Context

**Language/Version**: C# / .NET (repo default); TypeScript for Studio
**Primary Dependencies**: existing runtime scheduler, iteration-frame primitive (ADR 0028), value-flow validation
**Testing**: xunit only (no FluentAssertions); converter corpus tests; one e2e run of the JTest `single-outcome` suite
**Project Type**: modular framework (Design/Runtime split)
**Constraints**: Runtime never references Design (Elsa §E2.2); no new extension-point kind; no concurrency setting other than `MaxDegreeOfParallelism` (FR-009)

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
- `MaxDegreeOfParallelism` input (`int`, default 16). Range check (1 to 64) when evaluated, and the cap check (1,000
  items, `ParallelForEachCapExceeded`) before any pass is scheduled (FR-009a). The cap is the named constant
  `MaxParallelItems`, as large-fan-out.md names it, so the state-size benchmark can lower it in one place. The
  collection input keeps `ForEach`'s name `Collection`, where the large fan-out page says `Items`. One name across both
  loops wins, and T020 aligns the page. Designer validation for literal values goes in the activity's
  design-side validation, the same place `ForEach`'s literal checks live.
- `ExecuteStructureAsync`: resolve the collection once. If it is empty or there is no body, return `Complete()`.
  Otherwise schedule the passes for items `0 .. min(N, degree) - 1`, each with
  `StructuredExecutionIdentity.Iteration(parentExecutionId, index)` and a `LoopIterationScopeRequest`, then return
  `Defer`. Give `StructuredExecutionIdentity` a neutral iteration prefix, or a loop-kind parameter, so pass ids do not
  read `foreach-iteration`. Keep `ForEach`'s existing ids unchanged.
- `OnChildCompletedAsync`: read no pass rows per completion (FR-008). `Parallel`'s `ListAllByParentAsync` read returns
  every pass row on every completion, which is O(N²) for N items, so do not copy it. The collection itself comes
  from the parent's input snapshot, which is re-materialised on every callback
  (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:216-224`), as for `ForEach` (`ForEach.cs:106`). Only the
  one item being started is enveloped. At the 1,000-item cap this per-evaluation read is bounded, and T009 measures it.
  - A plain count of `Completed` rows is not enough. A child's `Completed` row commits in its own stage, and the
    parent's evaluation is a later work item (`WorkflowCompleteActivitySchedulerWorkHandler.cs:131-132,148-182`).
    With inline bodies, several rows can be `Completed` before the first evaluation runs. A formula on that count
    would skip items and schedule some twice. For example, with degree 2 over 3 items, both evaluations see 2
    completions and item 2 never runs.
  - So each evaluation reads one count, consistent with the evaluations before it. `p` is the number of passes whose
    completion the parent has already **processed**: distinct `IterationId`s with a `Completed`, non-superseded row that
    carries the `ParentCompletionProcessedWorkItemId` marker. The marker commits in the same commit as the parent's
    callback result and the schedule intents for the children it starts
    (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:640-660`; `ActivityAttemptActivationClaimer.cs:187-196`).
    The current child's marker is not yet visible: an already-marked child returns early at `:143`. A retried
    successor starts without the marker (`ActivitySuccessorStaging.cs:209`). Evaluations run one at a time under the
    drain's single-writer lease (`WorkflowDrainOrchestrator.cs:45`).
  - The started count is derived, not read. Child rows appear only when their schedule work items run
    (`WorkflowScheduleActivitySchedulerWorkHandler.cs:130-154`), so a row count could lag behind and start an item
    twice. Start schedules `min(N, degree)` items, and each processed completion starts at most one more, so after `p`
    processed completions the loop has started exactly `min(N, p + degree)`. `N` and `degree` come from the pinned
    input snapshot, so they do not change between evaluations.
  - Add the count as one member of `IActivityExecutionStateStore`, for example
    `CountProcessedCompletedChildIterationsAsync(workflowExecutionId, parentActivityExecutionId, executableNodeId)`.
    It is a count, never a row list. Implementations:
    - EF Core: `ActivityExecutionStateEntity` keeps the node id, the iteration id and the marker only inside
      `ContentJson` (`ActivityExecutionEntityConfigurations.cs:15-46`). Add columns for the executable node id, the
      iteration id and a "parent completion processed" flag, written on every upsert from the state, with an index that
      follows the existing hash-column pattern for MySQL's key limit (`ActivityExecutionEntityConfigurations.cs:28-31`):
      scope hash, workflow execution id hash, parent id hash, node id (bounded length or a hash), status, processed. Add a migration. Existing rows need no backfill: no row written before
      the migration can belong to a `ParallelForEach`, the only caller, and the project is pre-stable.
    - In-memory store.
    - `CoalescingActivityExecutionStateStore` (`CoalescingRuntimeStateStores.cs:85`): adjust the inner count as its
      `CountAsync` does (`:136-157`). First keep only the session upserts whose parent id and node id match; a row's parent and node never
      change, so no other upsert can affect the count, and FR-008's per-completion cost stays independent of
      unrelated session rows. For each kept upsert and each tombstone, look up the inner row, subtract it when it
      matched the predicate (node, `Completed`, processed, not superseded), and add the session row when it matches. Without this, a staged but unflushed marker is
      missed, `p` is too low, and an item is started twice.
    - Every other implementer of the interface, test doubles included (about ten in essentials, for example
      `ParallelActivityTests.cs:302` and several in `Workflows/Runtime/Tests`, plus the Agent, Etl and Rules tests).
    - One contract test shared by the EF Core, in-memory and coalescing stores, and the store's `EXTENSION_POINTS.md`.
    - If this proves heavier than planned, the measured fallback is the paged parent read (`ListByParentPageAsync`),
      whose cost the 1,000-item cap bounds. T009 records which was used.
  - Decision, with `c = p + 1`: if `c` equals `N`, complete with `Done`. Otherwise, if `p + degree < N`, schedule the
    pass for item `p + degree`, then defer. Every item is started exactly once, in item order, and at most `degree` run
    at a time, for inline and suspending bodies alike. The loop keeps no state of its own. A re-delivered completion is
    dropped once per child before the loop is called (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:143`,
    `WasParentCompletionProcessed`).
  - A faulted pass is never counted, so it holds its slot until its retry completes (US2 scenario 1). A retried pass's
    successor keeps the iteration through `source with` (`ActivitySuccessorStaging.cs:108`) and the provenance built in
    `RetryFaultedActivityAlterationHandler.cs:200-209`, so it counts once for its item. A superseded row is excluded by
    its own status (`ActivitySuccessorStaging.cs:176`).
  - The Break outcome needs no special case (FR-007).
- The retry path is `RetryFaultedActivity` under `WaitForIntervention` (`RetryFaultedActivityAlterationHandler.cs:119-125`).
  `RescheduleActivity` refuses a source that has a blocking incident (`RescheduleActivityAlterationHandler.cs:57-59`).
  Test that a retried pass keeps its `IterationId` and iteration frame and that its successor's completion completes
  the loop.

### Publish checks (US3)

- `LoopKind` gains `ParallelForEach` (iteration names `currentItem` and `currentIndex`, index switch `ExposeIndex`) and
  a flag `PassesUnordered`.
- `LoopStructureHandler` reports the body as unordered with itself when `PassesUnordered` is set (today it is always
  `FirstBeforeSecond`, lines 38-39), so `UnorderedVariableWriteAnalyzer` (lines 270-277) raises the spec 266 warning.
  Review `AssessChildResult` (lines 34-35) for the same assumption.
- `UnorderedVariableWriteAnalyzer.AnyAncestorOverlaps` walks to the root, whatever the variable's declaring scope.
  Give the self-overlap check the variable's `ScopeId` and stop the walk at the declaring node, so a write to a
  variable declared inside the body (one copy per pass) is not reported (FR-010). This refines spec 266; record it in
  the PR. The other users of `AnyAncestorOverlaps` (`AreUnordered`) take the same bound.
- Register `new LoopStructureHandler(LoopKind.ParallelForEach)` in `ActivitiesControlFlowFeature`.
- Spec 1222 completion and spec 1231 refusals follow from `LoopKind.IterationVariableNames`. Add tests to prove it.

### Converter (US4)

- `Elsa3BuiltInActivityMap.Entries.cs`: replace the `NoCounterpart("ParallelForEach", …)` entry with
  `Catalog("ParallelForEach", ParallelForEach, [Carry("Items", "Collection"), Slot("Body", "ParallelForEach.Body")])`,
  plus the no-body warning (FR-016) and the literal-value refusal (FR-017) as member rules.
- Per-pass names (FR-013): the owner is decided per name. Walk up from the read to the nearest Elsa 3 loop that
  declares that name on its body's context. For `CurrentValue`, that is `For`, `ForEach` or `ParallelForEach`. For
  `CurrentIndex`, it is `ForEach` or `ParallelForEach` only, because `For` declares `CurrentValue` alone
  (`For.cs:105-108`, `ForEachT.cs:97-98`, `ParallelForEachT.cs`). If the nearest one is not a `ParallelForEach`, keep today's handling for that loop
  (`Elsa3ValueFlowLowerer.cs:1157-1161`): the read is refused with `MissingAmbient` when no declared variable has that
  name, and binds to a declared variable of that name when one exists. That silent bind is an existing gap of the
  `For`/`ForEach` conversion, which this spec does not fix. It is reported as a follow-up. The `Variable` case of `Elsa3ValueFlowLowerer.ResolveAmbient` (lines 1108-1163) resolves only variables declared
  in the definition, so `CurrentValue` returns `MissingAmbient` today, and a workflow variable of that name would match
  first. Add a resolution branch for `CurrentValue` and `CurrentIndex` that runs before workflow and enclosing-scope
  variables when the reference sits in a `ParallelForEach` body. It yields a `VariableExpressionParameterBinding` to
  `currentItem` or `currentIndex` with the loop node as declaring scope. Only a variable declared by a scope inside the
  body is checked first. Add the Elsa 4 names to `AncestorScopeNames` for the shadowing checks, as for `ForEach`. A
  computed name (`getVariable(expr)`) cannot be resolved this way: refuse it inside a `ParallelForEach` body when the
  expression could yield one of the two names. That is every computed name, since it is not provable. The guidance
  asks for a literal name.
- Writes to those names: refused in the expression rewriter's setter path with `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`
  (FR-014).
- `Elsa3ActivityNormalizer.ApplyEntryWarnings`: the Break warning's ancestor test also accepts the `ParallelForEach`
  type key, with a message variant for it (FR-015).
- Literal `Items` (FR-017): a member rule that refuses any non-null Literal or Object expression on `Items`.
- New behaviour codes in `Elsa3ActivityDiagnosticCodes`, with messages, also added to the spec 236 diagnostic-codes
  contract: `ELS3-BEHAVIOUR-PARALLEL-FOREACH-NO-BODY` (FR-016) and `ELS3-BEHAVIOUR-PARALLEL-FOREACH-BOUNDED` (FR-012,
  one per converted loop, naming the default degree and the cap).
- Remove `"Elsa.ParallelForEach"` from any list that treats it as unsupported. Keep the value-flow lowerer's message
  for its item reads (`Elsa3ValueFlowLowerer.cs:1663`).

### Studio (US5)

- `blockPromotion.ts`: add the activity type to the safe activities and `elsa.parallelforeach.structure` to the loop
  kinds.
- Toolbox, body-slot editing and variable completion come from the activity descriptor and structure kind. Verify them
  in the composed host per Studio UX acceptance, with screenshots for the PR.
- Instance view: passes show under the loop by iteration id, as `ForEach` passes do, and the retry action on a faulted
  pass uses the existing `RetryFaultedActivity` alteration.

### Docs and evidence

- `ControlFlow/ParallelForEach/README.md` (FR-020), the control-flow `EXTENSION_POINTS.md`, and the module README's
  activity list.
- `docs/architecture/large-fan-out.md`: the "What exists today" row that says Elsa 4 has none, and the effect-key row
  that says the parallel activity is not built (FR-020).
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
essentials/src/Workflows/Runtime/Core/Contracts/IActivityExecutionStateStore.cs   changed (count member)
  + EF Core (columns, index, migration), in-memory and coalescing implementations, other implementers, contract test
extensions/Elsa3/src/Mapping/...                         changed (map entry, rewrite, normalizer, codes)
extensions/Elsa3/tests/Mapping/Tests/                    new cases
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/blockPromotion.ts   changed
```

## Complexity Tracking

- The store count member is a new contract member with three store implementations (EF Core, in-memory,
  coalescing) and updates to every other implementer. It is the smallest way to meet FR-008
  without giving structural activities a state bag of their own.
- The collection helper extracted from `ForEach` keeps the two loops from drifting apart.
