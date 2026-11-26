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

## Phase 2: Runtime activity (US1, US2)

- [ ] T004 [US1] Tests first in `essentials/tests/Activities/ControlFlow/Tests/ParallelForEach/`: all passes scheduled
  at start in item order; each pass reads its own `currentItem`/`currentIndex`; empty, null and no body complete with
  `Done`; a string, object or number faults with the shared message; nested loops read the right item; per-pass body
  container variables are isolated.
- [ ] T005 [US1] Implement `ParallelForEach` (plan "Runtime activity"): scheduling, O(1)-per-completion counting of
  completed passes by iteration id (store count query or durable counter), Break outcome counted as completed.
- [ ] T006 [US1] Runtime test: three passes suspended on bookmarks are all started before any resumes, each resumes on
  its own bookmark, and the loop completes after the last one. Also a restart mid-run counts no pass twice.
- [ ] T007 [US2] Tests under `WaitForIntervention`: one faulted pass leaves the loop running and the others complete;
  `RetryFaultedActivity` on the faulted pass keeps its iteration id and frame, and the loop then completes with
  `Done`.
- [ ] T008 [US2] Tests: under the default strategy the instance faults and the loop is marked `Faulted`; under
  `ContinueWithIncidents` an unretried pass leaves the loop running with the incident open (documented, US2
  scenario 4).
- [ ] T009 Performance check (SC-003, SC-004): 10 items with a 1 s delay finish under 3 s; 1,000 inline items scale
  linearly against 100. Record the numbers in the PR.

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

- [ ] T013 [US4] Map `Elsa.ParallelForEach` (FR-012) and update the activity-map tests that expected
  `ELS3-ACTIVITY-NO-COUNTERPART`.
- [ ] T014 [US4] Per-pass names (FR-013): a resolution branch in `Elsa3ValueFlowLowerer.ResolveAmbient` ahead of
  workflow and enclosing-scope variables; `getVariable("CurrentValue"|"CurrentIndex")` and the other by-name read forms;
  a workflow variable named `CurrentValue` does not win; a body-declared one does; a computed name is refused.
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

- [ ] T020 README and `EXTENSION_POINTS.md` for the new activity (FR-020); control-flow module README list.
- [ ] T021 Regenerate the activity contract surface baseline and the Elsa 4 parity surface evidence (FR-021).
- [ ] T022 Run the JTest `single-outcome` suite on Elsa 4; record the ParallelForEach steps in the S-15 ledger
  (SC-002); name remaining blockers with their issues.
- [ ] T023 Set this spec's status to Implemented in the implementing PR; close the #1152 row and #5832.

## Dependencies

Phase 1 → Phase 2 → Phases 3 and 4 (independent of each other) → Phase 5 → Phase 6. Spec 1242's waiting path
depends on Phase 2 and Phase 4 being merged.
