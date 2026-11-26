# Tasks: ParallelForEach runs one body pass per item at the same time

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. Build and test only the projects touched; leave the full solution to CI.

## Phase 1: Shared loop pieces (blocks everything)

- [ ] T001 Extract `ForEach`'s collection resolution and item envelopes into `ControlFlow/Loops/LoopCollection.cs`,
  with the activity name as a message parameter; switch `ForEach` to it. The existing ForEach tests stay green with
  unchanged messages.
- [ ] T002 Give `StructuredExecutionIdentity` an iteration id for the new loop without changing `ForEach`'s ids; unit
  test both forms round-trip through `TryReadIteration`.
- [ ] T003 Add `LoopKind.ParallelForEach` (iteration names, `ExposeIndex` switch, `PassesUnordered = true`).
- [ ] T003a Add `CountProcessedCompletedChildIterationsAsync` (completed iterations carrying the
  `ParentCompletionProcessedWorkItemId` marker) to `IActivityExecutionStateStore`, implemented in the EF Core store
  (index and migration) and the in-memory store, with a shared contract test (distinct iteration ids, superseded rows
  excluded, unprocessed completions not counted); update the store's `EXTENSION_POINTS.md`.

## Phase 2: Runtime activity (US1, US2)

- [ ] T004 [US1] Tests first in `essentials/tests/Activities/ControlFlow/Tests/ParallelForEach/`: the first
  `min(N, degree)` passes scheduled at start in item order, then more as slots free up. With inline bodies (completions
  evaluated late), degree 2 over 3 and over 5 items: every item runs exactly once and never more than 2 at a time; each pass
  reads its own `currentItem`/`currentIndex`; a JSON `null` item is an explicit null; empty, null and no body complete
  with `Done`; a string, object or number faults with the shared message; 1,001 items fault with
  `ParallelForEachCapExceeded` before any pass; a degree of 0 or 65 faults; nested loops read the right item; per-pass
  body container variables are isolated; a body ending with `Break` counts as completed and the loop neither ends
  early nor cancels other passes (FR-007).
- [ ] T005 [US1] Implement `ParallelForEach` (plan "Runtime activity"): cap and degree checks, scheduling under the
  degree, O(1)-per-completion counting through T003a.
- [ ] T005a [US1] Designer validation for a literal collection above the cap and a literal degree outside 1 to 64
  (FR-009a), with tests.
- [ ] T006 [US1] Runtime test: three passes suspended on bookmarks are all started before any resumes, each resumes on
  its own bookmark, and the loop completes after the last one. Also a restart mid-run counts no pass twice, and an
  instance cancel cancels every running pass.
- [ ] T007 [US2] Tests under `WaitForIntervention`: one faulted pass leaves the loop running and the others complete;
  with degree 1 the faulted pass holds the only slot until retried;
  `RetryFaultedActivity` on the faulted pass keeps its iteration id and frame, and the loop then completes with
  `Done`.
- [ ] T008 [US2] Tests: under the default strategy the instance faults and the loop is marked `Faulted`; under
  `ContinueWithIncidents` an unretried pass leaves the loop running with the incident open (documented, US2
  scenario 4).
- [ ] T009 Performance check (SC-003, SC-004): 10 items with a 1 s delay finish under 3 s. Measure 100 and 1,000
  inline items, with the share spent re-reading the collection snapshot per evaluation, and record the numbers in the
  PR.

## Phase 3: Publish checks (US3)

- [ ] T010 [US3] `LoopStructureHandler` reports the body as unordered with itself for `PassesUnordered`; review
  `AssessChildResult`. Register the handler in `ActivitiesControlFlowFeature` with a registration test.
- [ ] T010a [US3] Bound `UnorderedVariableWriteAnalyzer.AnyAncestorOverlaps` at the variable's declaring scope (FR-010,
  spec 266 refinement); spec 266's existing tests stay green.
- [ ] T011 [US3] Validation tests: an outer-variable write in the body gives exactly one `ValueFlow/ConcurrentWrite`
  warning naming the node; body-local writes give none; `currentItem`/`currentIndex` assignment is refused;
  `ExposeIndex = false` refuses `currentIndex` reads.
- [ ] T012 [US3] Runtime tests for ADR 0092 decision 10 and the stale-base case with two passes (spec US3 scenario 3).

## Phase 4: Converter (US4)

- [ ] T013 [US4] Map `Elsa.ParallelForEach` (FR-012) with the `ELS3-BEHAVIOUR-PARALLEL-FOREACH-BOUNDED` warning per
  loop; update the activity-map tests that expected `ELS3-ACTIVITY-NO-COUNTERPART`.
- [ ] T014 [US4] Per-pass names (FR-013): a resolution branch in `Elsa3ValueFlowLowerer.ResolveAmbient` ahead of
  workflow and enclosing-scope variables; `getVariable("CurrentValue"|"CurrentIndex")` and the other by-name read forms;
  a workflow variable named `CurrentValue` does not win; a body-declared one does; an Elsa 3 `For` or `ForEach`
  between the read and the loop keeps today's handling; a computed name is refused.
- [ ] T015 [US4] Refuse writes to the per-pass names (FR-014).
- [ ] T016 [US4] Break warning with a `ParallelForEach` ancestor (FR-015); no-body behaviour code and message (FR-016),
  added to the spec 236 diagnostic-codes contract; refusal of every non-null literal `Items` (FR-017).
- [ ] T017 [US4] Convert `single-outcome-activities.json` in a converter test: no NO-COUNTERPART, loop and slot as
  expected (SC-001).

## Phase 5: Studio (US5)

- [ ] T018 [US5] `blockPromotion.ts`: safe activity and loop kind; Studio unit test.
- [ ] T019 [US5] Composed-host check per Studio UX acceptance: toolbox, body slot, `variables.` completion, instance
  view per pass with retry. Screenshots on the PR.

## Phase 6: Docs, evidence, close-out

- [ ] T020 README and `EXTENSION_POINTS.md` for the new activity (FR-020); control-flow module README list;
  `docs/architecture/large-fan-out.md`: the "What exists today" row (line 29) that says Elsa 4 has none, the
  in-instance effect-key row (line 240, proposed → built, with the new iteration id form), and the release 1 section
  (lines 250-256) in the present tense with the input named `Collection`.
- [ ] T021 Regenerate the activity contract surface baseline and the Elsa 4 parity surface evidence (FR-021).
- [ ] T022 Run the JTest `single-outcome` suite on Elsa 4; record the ParallelForEach steps in the S-15 ledger
  (SC-002); name remaining blockers with their issues.
- [ ] T023 Set this spec's status to Implemented in the implementing PR; close the #1152 row and #5832.

## Dependencies

Phase 1 → Phase 2 → Phases 3 and 4 (independent of each other) → Phase 5 → Phase 6. Spec 1242's waiting path
depends on Phase 2 and Phase 4 being merged.
