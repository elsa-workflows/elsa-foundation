# Tasks: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec. Phase 1 (the activity) comes first. Phase 2 (the
converter) needs the activity in the catalog.

## Phase 1: The Elsa 4 activity (US1, US2)

- [ ] T001 Decide where the `Dispatch` slot check runs (the handler's compile step or a submitted-tree validator),
  and whether the catalog's initial payload can start the slot with an empty `DispatchWorkflow` (plan §1). Check which
  FR-024 pieces exist on the initiative branch (the neutral iteration-id prefix, `LoopCollection`); reuse them if they
  do. Record the outcome in the PR.
- [ ] T002 [US1] Add `BulkDispatchWorkflows` with `Items`, the three slots, type key, cap and window constants and `Done` outcome in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/` (FR-001–FR-003, FR-009).
- [ ] T003 [US1] Add the structure handler for the three slots and the `Dispatch` slot check (FR-003). Register it in
  the DispatchWorkflow features. `DispatchPinSource` is not changed.
- [ ] T004 [US1] [US2] Scheduling and completion:
  - `Dispatch` once per item with iteration id and scope, at most 16 passes open (FR-004, FR-005);
  - routing by outcome and completion, with the ended-index state of plan §2 (FR-006);
  - the cap (FR-007);
  - faults: absorb a `Dispatch` fault when `ChildFaulted` is present, otherwise intervene and defer (FR-008).
- [ ] T005 [P] [US1] [US2] Runtime tests (plan, Test scope: Runtime).
- [ ] T006 [P] [US1] Check in Studio that the designer shows the three slots through the generic rendering, and that
  the instance view shows the per-item `Dispatch` executions. Add no Studio-specific code (FR-003).
- [ ] T007 [P] Write the README section and update `EXTENSION_POINTS.md` (FR-023).

## Phase 2: Converter (US3, US4)

- [ ] T008 [US3] Map entry for `Elsa.BulkDispatchWorkflows`, with the ports added to the surface fixture. Add a node
  rewrite that emits the `{elsa3NodeId}.dispatch` `DispatchWorkflow` into `Dispatch`, with the member rules of
  FR-012–FR-014 on it (FR-011).
- [ ] T009 [US3] Node rewrite for the per-item `Inputs` and `CorrelationId` (FR-015, FR-016), with item-read
  resolution in `Elsa3ValueFlowLowerer`. Add `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN` and
  `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` here.
- [ ] T010 [US3] Outcome step in `Elsa3FlowchartGraphBuilder` (FR-017). Add `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE`
  here.
- [ ] T011 [US4] Child-result reads in the slots (FR-019). Add `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` here.
- [ ] T012 Raise `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED` on every conversion (FR-020). Add it here.
- [ ] T013 [P] [US3] [US4] Converter tests (plan, Test scope: Converter). Remove `no-counterpart:BulkDispatchWorkflows`
  from `Elsa3ConversionDiagnosticsTests` and keep ParallelForEach.
- [ ] T014 [P] Update the converter README and the spec 236 member map and diagnostic-code contract (FR-021, FR-023).
- [ ] T015 Convert `performance-concurrency.json` (with its child) and the composition root locally, and hand FR-022
  over to S-15 through the shared notes. Ask S-15 to run the concurrency suite and record the 300-children result.

## Phase 3: Finish

- [ ] T016 Run the scoped tests and the gate checker, and set the spec status to Implemented in the PR that finishes
  phase 2.
