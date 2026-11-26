# Research: ParallelForEach runs one body pass per item at the same time

Sources: elsa-core tag `3.6.3` (local clone), this repository at initiative head `52270b8747`, and the S-15 JTest
corpus on `agent/claude/3052-s15-jtest-impl`.

## R-1 Elsa 3 ParallelForEach

`src/modules/Elsa.Workflows.Core/Activities/ParallelForEachT.cs`:

- `Items` (`Input<object>`) and a `Body` port. No outputs. `ParallelForEach` is `ParallelForEach<object>`.
- In `ExecuteAsync`, for each item it creates two variables, `CurrentValue` (the item) and `CurrentIndex` (the index),
  both with the instance storage driver. It schedules `Body` with them and a fresh `Guid` tag, and records the tags as
  `ScheduledTagsProperty`. With no tags, it completes at once.
- `OnChildCompleted` adds the completed tag and completes once the completed set equals the scheduled set.
- There is no `Break` behaviour and no fault handling.

`ItemSourceActivityExecutionContextExtensions.GetItemSource<T>`: null yields nothing. Async enumerables and
`IEnumerable<T>` yield their items. Anything else, including a string (which is `IEnumerable<char>`, not
`IEnumerable<object>`), yields nothing, silently. A literal `Items` always arrives as a string. The literal
descriptor deserializes through `JsonElementExtensions.GetValue` (`Elsa.Expressions/Extensions/JsonElementExtensions.cs`),
which returns `GetRawText()` for an object or an array, and the object target keeps the string. So a literal list ran
zero passes.

Unit and integration tests (`test/unit/.../Looping/ParallelForEachTests.cs`,
`test/integration/.../ParallelForEachTests.cs`) confirm: one scheduled body per item, empty and null complete at once,
mixed item types work, and with one faulting item the other items still run. With a null body nothing is scheduled
(`ActivityExecutionContextSchedulerStrategy` returns for a null activity). The tags stay unmatched, so the loop never
completes.

## R-2 Elsa 3 concurrency

`ActivitySchedulerFactory` creates a `QueueBasedActivityScheduler` (the stack-based one is commented out). All passes
are queued in item order and run interleaved on one thread. A pass that creates a bookmark ends its turn, and the
next queued work runs. Elsa 3 therefore has no true multi-threading here, and Elsa 4's single queue per execution
(ADR 0092, `Parallel.cs` remarks) is the same model.

## R-3 Elsa 3 Break

`Break` sends a `BreakSignal`. `BreakBehavior` is attached only to `For`, `ForEach` and `While`, and `Fork` and
`Sequence` handle it themselves. `Sequence` (`Sequence.cs:65`) marks itself as breaking but does not stop the signal.
`ParallelForEach` has neither, so the signal bubbles past it. Every `Sequence` on the way up ends early, up to and
including the nearest enclosing loop.
The converter already warns `ELS3-BEHAVIOUR-BREAK-STOPS-AT-PARALLEL` for a `Break` below `Parallel`
(`Elsa3ActivityNormalizer.ApplyEntryWarnings`). FR-015 adds `ParallelForEach` to that ancestor test.

## R-4 What the JTest suite asserts

`single-outcome-activities.json`: `ParallelForEach1` with `Items = return getRequestBody().parallelForEachLoop.items`
(typeName `Object`) and a `Flowchart` body holding one `WriteLine` with `text: null`.
`single-outcome-tests.json` ("Assert: ParallelForEach execution as expected") checks that the loop's status is
`Completed`, that `activityState.Items[0..1]` equal the two request items, and that the body `WriteLine` completed
`expectedIterationCount` (2) times. The ledger rows: `dispositions.md` line 40 (refused, NO-COUNTERPART) and
`suites.md` line 47 (pass on 3.6.3, incompatible on Elsa 4).

## R-5 Elsa 4 foundations

- **ForEach** (`essentials/src/Activities/ControlFlow/ForEach/Activities/ForEach.cs`) schedules one pass at a time
  with `ScheduleChildActivity(..., ActivitySchedulingProvenance(iterationId), new LoopIterationScopeRequest(...))` and
  advances in `OnChildCompletedAsync`. Its collection resolution (`ForEachCollection`) and envelope rules (refusing a
  transient collection, and external or custom storage) are what a parallel loop needs too.
- **Several passes of one node at once.** Every schedule request gets its own `ActivityExecutionId`. The work-item,
  command and idempotency ids derive from `schedule-child:{nodeId}:{childActivityExecutionId}`
  (`essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/SchedulerWorkItems.cs:128,182-187`), and
  `ScheduleChildActivity` only appends (`SimpleActivityExecutionContext.cs:125-140`). Iteration frame ids include the
  body execution id (`VariableFrameFactory.cs:34,52`). Nothing deduplicates by node.
  `StructuredExecutionIdentity.Iteration` can be reused, though its prefix says `foreach-iteration`.
- **Parallel** schedules all branches at once and counts finished branches from the durable store
  (`ListAllByParentAsync`), grouped by node (`Parallel.cs:199-226`). A parallel loop must group by iteration id,
  because every pass has the same node. No shared "N children, done when all done" helper exists.
- **Faults.** A parent learns of a child fault only through `IRuntimeActivityChildFaultHandler`. `Parallel` faults the
  composite once its threshold is unreachable. `Sequence` does not handle faults. Both implement
  `IRuntimeAcceptsRescheduledFaultedChild`, but a `Parallel` that has already faulted is no longer running.
  The retry path is `RetryFaultedActivity`, which requires the `WaitForIntervention` outcome
  (`RetryFaultedActivityAlterationHandler.cs:119-125`). `RescheduleActivity` refuses a source with a blocking incident
  (`RescheduleActivityAlterationHandler.cs:57-59`). Under `ContinueWithIncidents` the incident becomes `Open` and the
  backstop leaves it alone, so an unretried pass keeps the loop running, as for a `Sequence` child. There is no
  alteration that cancels a single activity. Rescheduling a faulted child is refused unless the parent carries that
  marker **and is still running** (`IRuntimeStructuralActivity.cs:37-44`). Nothing cancels siblings on a fault
  (`StructuralParentEvaluationSupport.cs:162-163`). With the default instance strategy, an activity fault faults the
  instance. Otherwise an undecided blocking incident at quiescence faults it (`docs/runtime-fault-behavior.md`).
  This is why FR-006 follows `Sequence`.
- **Variables.** Blind writes apply and the last commit wins. A stale base applies with a lost-update warning (ADR 0092
  decisions 5 and 6). Inline steps are gap-free (decision 10). Loop item and index are read-only (spec 1231 FR-006).
  `UnorderedVariableWriteAnalyzer.cs:270-277` already warns for a node that overlaps itself, but only when the parent's
  structure handler reports the body as unordered with itself. `LoopStructureHandler.cs:38-39` reports
  `FirstBeforeSecond` for every loop today. `AnyAncestorOverlaps` walks to the root without looking at the variable's
  declaring scope, so it would also flag writes to variables declared inside the body (one copy per pass), which
  needs a bound (FR-010).
- **Completion cost.** `ListAllByParentAsync` returns all child rows. Calling it on every one of N completions is
  O(N²), which is acceptable for a handful of `Parallel` branches, not for a list of items.
- **Background.** ADR 0093 C1 requires a leaf, so a structural loop is never a candidate. Leaf body steps keep C1–C10.
- **Converter.** `Elsa3BuiltInActivityMap.Entries.cs:55` maps ParallelForEach as `NoCounterpart`.
  `Elsa3LoopVariableReferencesRewrite` maps ForEach's `CurrentValue` **output** to `currentItem`. Elsa 3 `For` and
  `ForEach` also declare per-pass `CurrentValue` and `CurrentIndex` variables on their body (`ForEachT.cs:97-100`,
  `For.cs:107`), and the converter keeps refusing by-name reads of those (`Elsa3ValueFlowLowerer.cs:1127-1137`). So the
  nearest Elsa 3 loop of any kind owns a by-name read. Elsa 3 ParallelForEach has no output, so its reads go through
  `getVariable("CurrentValue")`. `Elsa3ExpressionRewriter`
  hands that to the ambient resolver (`AddResolvedEdit`). `Elsa3ValueFlowLowerer.ResolveAmbient` (lines 1106-1162)
  knows only declared variables, so the run-time name is missing today, and a workflow variable named `CurrentValue`
  would match first. `Elsa3DefinitionConverter.AncestorScopeNames` carries Elsa 4 names for shadowing checks only.
  The converter needs a resolution branch for the per-pass names (plan, Converter).

## R-6 Placement

The control-flow project `essentials/src/Activities/ControlFlow` holds `ForEach`, `For`, `While`, `Do` and `Parallel`,
sharing `Loops/` (`LoopKind`, `LoopNavigator`, `LoopStructureHandler`). It registers one structure handler per loop in
`ActivitiesControlFlowFeature.cs:37-40`. A new loop adds a `LoopKind`, a folder `ParallelForEach/`, and a handler
registration. Studio lists loop structure kinds and safe activities in
`Studio/src/essentials/Elsa.Studio.Workflows/Client/src/graph-authoring/blockPromotion.ts:20,25`.

## R-6a The owner's release 1 decision

`docs/architecture/large-fan-out.md` ("Release 1: a documented cap", decisions 1, 2 and 5) and
`docs/architecture/decisions.md` (row "ForEach cap") are an approved design (gate 2, 18 November 2026). Release 1 gets
a parallel ForEach that runs its bodies inside one instance, with `Items` capped at 1,000 (`ParallelForEachCapExceeded`)
and `MaxDegreeOfParallelism` (default 16, 1 to 64). The reason is one instance's state and inbox. The distributed form
is an explicit option after release 1, and a parked child keeps its parent waiting. The page's "What exists today"
still says Elsa 4 has no parallel ForEach. Spec 1243 adopts decision 1 and leaves decision 2 for later.

With a degree, the loop must know how many passes it has started and how many completions it has processed. The
store's plain `Completed` count runs ahead of the parent's evaluations, because a child's row commits before the
parent's evaluation work item runs. The `ParentCompletionProcessedWorkItemId` marker is what records that a completion
was processed. See plan, "Runtime activity". `IActivityExecutionStateStore` has no count query today. It has only list
and page reads (`ListByParentPageAsync`, line 58).

## R-7 ADR need

None. ADR 0028 defines the per-pass frame for "a loop activity", and its isolation is by frame instance, which holds
for concurrent passes. ADR 0092 and spec 266 already cover unordered writes. The structure handler has to report the
new ordering, and spec 266's self-overlap check needs a bound at the declaring scope, which is a spec-level
refinement, not a decision change. The cap and degree come from an existing owner decision (R-6a), which the spec
adopts rather than changes. The fault rule reuses an existing runtime capability. Join semantics are excluded (#4193).
