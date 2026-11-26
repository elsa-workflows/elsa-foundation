# Tasks: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec. Phases 1–3 do not depend on spec 1243. Phase 4 waits
for spec 1243's parallel ForEach.

## Phase 1: Rewrite skeleton (US1)

- [ ] T001 [US1] Resolve research item R-1 (plan §1): can an `IElsa3NodeRewrite` replace a node with a structural node
  that has a body? Record the answer in the PR, and move the lowering into `Elsa3FlowchartGraphBuilder` if it cannot.
- [ ] T002 [US1] Add `Elsa3NodeRewriteKind.BulkDispatchAsLoop`, and replace the `NoCounterpart("BulkDispatchWorkflows")`
  entry in `extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`.
- [ ] T003 [US1] Add `Elsa3BulkDispatchAsLoopRewrite`. For a literal-false wait, emit a `ForEach` with a
  `DispatchWorkflow` body (FR-002 and FR-004, with the ids from plan §2). For every other wait, raise `NoCounterpart`
  with guidance naming spec 1243.
- [ ] T004 [US1] Outcomes: `Done` → loop `Done`. Drop `Canceled` and `Completed` with info (FR-012, no-wait rows), and
  drop the child branches with info (US1 scenario 3).
- [ ] T005 [P] [US1] Tests in `Elsa3BulkDispatchLoweringTests`: the ForEach shape, kept ids, outcomes and dropped
  branches.

## Phase 2: Members (US3, US4)

- [ ] T006 [US3] Per-item `Inputs` expression for a known target and an unknown target (FR-006, plan §3), with warning
  `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` (FR-011).
- [ ] T007 [US3] Correlation function: substitute `get{DefaultItemInputKey}()`, then apply the JavaScript rewrite.
  Handle the absent and literal cases (FR-008).
- [ ] T008 [US4] Member rules: `WorkflowDefinitionId` literal only, `DefaultItemInputKey` literal only, and
  `StartNewTrace`/`ChannelName` Removed. `CancelChildOnParentCancellation` is synthetic false (FR-005, FR-007,
  FR-009).
- [ ] T009 [P] [US3] [US4] Tests:
  - inputs for map, scalar, null and array items, run through the Elsa 4 evaluator;
  - a case-mismatched member gets the declared spelling;
  - an undeclared default key is omitted;
  - correlation with `getItem()` and with a custom key;
  - each member refusal.

## Phase 3: Diagnostics and docs

- [ ] T010 Add the 3 codes to `Elsa3ActivityDiagnosticCodes` (FR-013), and update `Elsa3ActivityMapCoverageTests` and
  `Elsa3ConversionDiagnosticsTests`. ParallelForEach stays in the no-counterpart cases.
- [ ] T011 [P] Update `extensions/Elsa3/src/Mapping/README.md`, `specs/236-elsa3-activity-map/member-map.md` and
  `specs/236-elsa3-activity-map/contracts/diagnostic-codes.md` (FR-013, FR-014).
- [ ] T012 [P] Add the "one child per item" section to the `DispatchWorkflow` README (FR-015).
- [ ] T013 Convert `performance-concurrency.json` from the S-15 corpus locally. Confirm that it publishes and record the
  result (SC-001). Hand FR-016 over to S-15 through the shared notes.

## Phase 4: Waiting path (US2) — after spec 1243

- [ ] T014 [US2] For a wait that is absent, a literal true or an expression, emit the parallel ForEach with a flowchart
  body: dispatch `Completed`/`Faulted` → the converted branches (FR-003). Use 1243's current-item read in T007's
  substitution if it differs from `variables.currentItem`.
- [ ] T015 [US2] Outcomes in the waiting case: the `Completed` rows of FR-012, including
  `ELS3-ACTIVITY-BULK-DISPATCH-COMPLETED-ROUTE`; warning `ELS3-BEHAVIOUR-BULK-DISPATCH-PARALLEL-LIMIT` only if spec 1243
  added a cap (FR-010).
- [ ] T016 [US2] Child-result reads in the branches → `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` (FR-012).
- [ ] T017 [P] [US2] Tests:
  - the parallel shape;
  - an expression wait that evaluates false runs no branch;
  - a cancelled child runs no branch;
  - each `Completed` row;
  - each child-result read.
- [ ] T018 [US2] End-to-end conversion of a synthetic `acme.*` waiting definition. Hand the adjusted composition root
  over to S-15 (SC-002).

## Phase 5: Finish

- [ ] T019 Run the scoped tests and the gate checker, then set the spec status to Implemented in the PR that finishes
  phase 4. If phase 4 is split off, set the status in the PR that finishes phase 3, with phase 4 noted as pending.
