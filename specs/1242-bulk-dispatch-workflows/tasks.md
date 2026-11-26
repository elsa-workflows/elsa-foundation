# Tasks: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec. Phase 1 (the activity) comes first. Phase 2 (the
converter) needs the activity in the catalog.

## Phase 1: The Elsa 4 activity (US1, US2)

- [ ] T001 Resolve research items R-1 (hidden node bindings) and R-2 (same node scheduled several times in one turn)
  (plan §1, §2). Check with the spec 1243 session whether its concurrent scheduling exists yet (FR-024). Record the
  outcome in the PR.
- [ ] T002 [US1] Add `BulkDispatchWorkflows` with its inputs, slots, type key, cap constant and `Done` outcome in
  `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/` (FR-001–FR-003, FR-009).
- [ ] T003 [US1] Add the structure handler that keeps the hidden `DispatchWorkflow` node and binds its inputs (FR-004).
  Register it in the DispatchWorkflow features.
- [ ] T004 [US1] [US2] Scheduling and completion:
  - one dispatch child per item with iteration id and scope, all in one turn (FR-004, FR-005);
  - wait and no-wait completion and branch routing (FR-006);
  - the cap (FR-007);
  - slot faults (FR-008).
- [ ] T005 [P] [US1] [US2] Runtime tests (plan, Test scope: Runtime).
- [ ] T006 [P] [US1] Check in Studio that the activity shows its two slots through the generic rendering, and that the
  hidden node is not shown. Add no Studio-specific code (FR-003).
- [ ] T007 [P] Write the README section and update `EXTENSION_POINTS.md` (FR-023).

## Phase 2: Converter (US3, US4)

- [ ] T008 [US3] Map entry for `Elsa.BulkDispatchWorkflows`, with the member rules of FR-012–FR-014 and the ports added
  to the surface fixture (FR-011).
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
