# Tasks: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec. Phase 1 (the activity) comes first. Phase 2 (the
converter) needs the activity in the catalog.

## Phase 1: The Elsa 4 activity (US1, US2)

- [ ] T001 Check whether spec 1243 has already moved the iteration-id or collection helpers to a shared runtime
  project (FR-024). Reuse them if so; otherwise keep the local helpers.
- [ ] T002 [US1] Add `BulkDispatchWorkflows` with `Items`, the three slots, the runtime type and catalog key, the cap
  and window constants and the `Done` outcome in `essentials/src/Activities/DispatchWorkflow/Runtime/Activities/`
  (FR-001–FR-003, FR-009).
- [ ] T003 [US1] Add the structure handler: the three slots, `ProjectIterationVariables` for `currentItem` and
  `currentIndex`, `CompletesAfterItsChildren` true, `Unordered` for every child pair, and `AssessChildResult` per plan
  §1: different children → `Unknown`, `MayRunConcurrently = true`, `EarlierProducerRunCanReachConsumer = true`; a child
  with itself → `EarlierProducerRunCanReachConsumer = false`. Add the `BulkDispatch/DispatchSlot` draft validator, with a
  Validations.Core reference (FR-003, plan §1). Register both in the DispatchWorkflow features. `DispatchPinSource` is
  not changed.
- [ ] T004 [US1] [US2] Scheduling and completion:
  - `Dispatch` once per item with iteration id and scope, at most 16 passes open (FR-004, FR-005);
  - routing by outcome and completion, with the ended-index state of plan §2 (FR-006);
  - the cap (FR-007);
  - faults: no fault handler, so the incident strategy decides; never absorb (FR-008);
  - the `Items` durable-binding treatment, and slots scheduled with the pass's iteration id and their own
    `LoopIterationScopeRequest` (plan §2).
- [ ] T005 [P] [US1] [US2] Runtime tests (plan, Test scope: Runtime).
- [ ] T006 [P] [US1] Check in Studio that the designer shows the three slots through the generic rendering, and that
  the instance view shows the per-item `Dispatch` executions. Add no Studio-specific code (FR-003).
- [ ] T007 [P] Write the activity README section, covering the cap, batching, branches, FR-010a and FR-010b, and update the
  DispatchWorkflow `EXTENSION_POINTS.md` (FR-023). Also update
  `essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`, whose `ProjectIterationVariables` entry says only the
  ForEach and For handlers implement it.

## Phase 2: Converter (US3, US4)

- [ ] T008 [US3] Map entry for the CLR key `Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows`:
  - `Items` (FR-014), and the two ports as `Structure(…, Port)`, added to the surface fixture as ports;
  - every dispatch member as `Structure(…, Elsa3MemberKind.Input)`;
  - `ELS3-BEHAVIOUR-BULK-DISPATCH-CAPPED` as the entry's behaviour warning (FR-011, FR-020). Add that code here.
- [ ] T009 [US3] The one rewrite, `BulkDispatchChild`, builds the `{elsa3NodeId}.dispatch` child as a ready-made plan, attached to the bulk node's
  `Activities` and `Slots` under `Dispatch`,
  with every member set explicitly and the source names mapped back (FR-012, FR-013, FR-015, FR-016, plan §3). It also:
  - refuses a non-null literal `Items` (FR-014);
  - adds the branches with `AddChild`, except on a literal-false wait (FR-018);
  - builds `Inputs` as an Elsa 4 expression bound to `currentItem`, with a literal or Object static `Input` as
    constant entries, and refuses an expression `Input` (plan §3);
  - records the wait classification in the plan field.

  Add the plan field for the item key, the item-read resolution in `Elsa3ValueFlowLowerer`, the `args.<key>`
  acceptance in `Elsa3ExpressionRewriter`, and the bulk key in `AncestorScopeNames` (plan §3). Add the bulk-only `BulkDispatchTargetInputs` field, filled by the materializer's pre-scan with same-import or published declarations (plan §3, R-3). The #5808 `DispatchTargetInputs` is unchanged. Add `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN` and
  `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` here.
- [ ] T010 [US3] Outcome step in `Elsa3FlowchartGraphBuilder` (FR-017). Add `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE`
  here.
- [ ] T011 [US4] Child-result reads in the slots (FR-019). Add `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ` here.
  Also add the outer-write behaviour warning (FR-020a) and `ELS3-BEHAVIOUR-BULK-DISPATCH-OUTER-WRITE` here. Test both cases: a branch writing a workflow variable warns, and a branch `Flowchart` writing its own variable
  does not.
- [ ] T012 [P] [US3] [US4] Converter tests (plan, Test scope: Converter), including item members matched to declared names regardless of case, and two members that differ only in case, and a `DefaultItemInputKey` matched to a declared name in another case. Remove `no-counterpart:BulkDispatchWorkflows`
  from `Elsa3ConversionDiagnosticsTests` and keep ParallelForEach.
- [ ] T013 [P] Update the converter README and the spec 236 member map and diagnostic-code contract (FR-021, FR-023). The README states the outer-write rule (FR-010a).
- [ ] T014 Convert `performance-concurrency.json` (with its child) and the composition root locally, and hand FR-022
  over to S-15 through the shared notes. Ask S-15 to run the concurrency suite and record the 300-children result.

## Phase 3: Finish

- [ ] T015 Run the scoped tests and the gate checker, and set the spec status to Implemented in the PR that finishes
  phase 2.
