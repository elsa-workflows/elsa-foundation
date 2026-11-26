# Tasks: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. `[P]` means the task can run in parallel with others (it touches different files).

## Phase 1: Runtime `Output` (US1; FR-001 to FR-005)

- [ ] T001 `DispatchWorkflowActivityResult`: add `Output` (`JsonElement?`,
  `[Output(Key = "Output", Path = "output", IsRequired = false)]`), left out of the serialized result when null (plan
  D-1). Contract test in `DispatchWorkflowContractTests`: the output is declared, it is optional, and the descriptor
  lists it.
- [ ] T002 Test first, in `DispatchWorkflowEndToEndTests`, on the real `WorkflowOutputSource` with default diagnostics
  settings. Fire-and-forget, faulted, cancelled and dispatch-failed
  children each give no `Output`. A variable that captures `Output` and holds `"before"` still holds `"before"` after
  each of them (FR-002, plan D-1).
- [ ] T003 Policy-free read of a child's committed workflow outputs (plan D-2). It selects the same outputs as
  `RuntimeWorkflowOutputStateProjection`, without `IRuntimePayloadCapturePolicy`, and leaves out sensitive-marked and
  not-inline values. Add unit tests beside the existing projection tests.
- [ ] T003a `WorkflowDispatchCompletionEnricher` sets `WorkflowDispatchParentResumePayload.Output` for `Completed` from
  T003, and payload equivalence includes it. Enricher tests: the value is replay-stable, a conflicting re-enrichment
  fails closed, and `Result.outputs` is unchanged.
- [ ] T003b `DispatchWorkflow.ResumeAsync` copies `payload.Output` into the activity result for `Completed`. End-to-end
  tests:
  - A child with output `Decision = "approved"` gives `{ "Decision": "approved" }`.
  - A 1,000-character `Summary` and an output named `token` arrive unchanged, while `Result.outputs` shows the
    diagnostic view.
  - A sensitive-marked output is left out.
  - Two declared outputs with one set give one property.
  - A child with no outputs gives `{}`.
- [ ] T004 [P] `DispatchWorkflowCheckpointTests`: a waiting parent resumed after a restart gives the same `Output`
  (FR-012).
- [ ] T005 [P] Check that `Output` is capturable in the Design descriptor and the Studio output list (FR-005). Change
  nothing if it already shows. Update `EXTENSION_POINTS.md` only if it lists outputs.

## Phase 2: Converter rules (US2 to US4; FR-006 to FR-011)

- [ ] T006 Dispatch Workflow: `Lost("Result")` becomes `CarryOutput("Result", "Output")` (FR-006). Converter tests: a
  `Result` capture with `WaitForCompletion` literal true, literal false and bound to an expression gives an `Output`
  capture into the same variable, with no `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`.
- [ ] T007 Outcomes (FR-007, FR-009). Dispatch Workflow: `Done` goes to `Dispatched`, `Completed` and `Faulted`.
  Execute Workflow: `Done` goes to `Completed` and `Faulted`. `Cancelled` stays unmapped. Converter tests pin the
  emitted connections.
- [ ] T008 Make `SemanticsChanged` message and guidance rule data, with the current HTTP text as the default.
  `ParsedContent` tests stay unchanged.
- [ ] T009 Execute Workflow: `SemanticsChanged("Result")` with the FR-010 message and guidance. A converter test
  asserts the code, both ways forward, and the read mapping (`Result.Output.X` to `Output.X`, `WorkflowInstanceId` to
  `ChildWorkflowExecutionId`, status to outcome).
- [ ] T010 Implement the `ExecuteWorkflowAsDispatch` rewrite (`ActivityMap/Rewrites/`) and register it in
  `Elsa3MappingFeature`, with a feature-registration test. `WaitForCompletion` becomes
  `Structure("WaitForCompletion", Elsa3MemberKind.Input)`, and the rewrite emits it with `SetProperty`, so
  `RemoveConsumedStructure` keeps it. The rewrite always emits literal `true` and raises
  `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` once. The warning adds the suspended-child sentence when the Elsa 3 value is
  literal false or bound (FR-008).
  - Tests: an unset value, literal true, literal false, a bound value and an explicit null. In each case the output is
    literal true and there is exactly one warning.
  - Update `A_null_ExecuteWorkflow_wait_keeps_spec_236s_mapping` and `Elsa3ConversionDiagnosticsTests` to the new
    rule.
- [ ] T011 [P] Update `Elsa3ActivityMapCoverageTests` and the `elsa3-activity-surface.json` fixture to the new rules.
  Find the tests that pinned the old rules and update them (SC-004). List them in the PR.

## Phase 3: Integration and records

- [ ] T012 Import integration (`DispatchWorkflowCallTests` with `dispatch-caller.json`): add an awaited Elsa 3 dispatch
  that captures `Result` and reads a child output by name. Import, publish and run it on the import host, then assert
  the value (SC-002 on the import path).
- [ ] T013 [P] Converter README (`extensions/Elsa3/src/Mapping/README.md`): add the FR-006 to FR-011 rows, the four
  accepted differences (a faulted child's outputs, a cancelled child, `ParentInstanceId`, output-name case), and
  Execute Workflow's always-wait.
- [ ] T013a [P] Amend spec 236's `member-map.md` rows for `DispatchWorkflow.Result`, the `Done` outcome and
  `ExecuteWorkflow` in place, citing spec 1245 (FR-011a).
- [ ] T014 Set the spec's status to `Implemented` in the implementation PR (not in a separate PR). Tell the S-15 session
  which adjusted-copy and ledger changes FR-013 allows. Do not edit the ledger here.
