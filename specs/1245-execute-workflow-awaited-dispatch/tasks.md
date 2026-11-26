# Tasks: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. `[P]` means the task can run in parallel with others (it touches different files).

## Phase 1: Runtime `Output` (US1; FR-001 to FR-005)

- [ ] T001 `DispatchWorkflowActivityResult`: add `Output` (`JsonElement?`,
  `[Output(Key = "Output", Path = "output", IsRequired = false)]`), left out of the serialized result when null (plan
  D-1). Contract test in `DispatchWorkflowContractTests`: the output is declared, it is optional, and the descriptor
  lists it.
- [ ] T002 Test first, in `DispatchWorkflowEndToEndTests`. Fire-and-forget, faulted, cancelled and dispatch-failed
  children each give no `Output`. A variable that captures `Output` and holds `"before"` still holds `"before"` after
  each of them (FR-002, plan D-1).
- [ ] T003 `DispatchWorkflow.ResumeAsync`: build `Output` for `Completed` from the safe entries of
  `payload.Result.Outputs`, leaving out the redacted ones. End-to-end tests:
  - A child with output `Decision = "approved"` gives `{ "Decision": "approved" }`.
  - Two declared outputs with one set give one property.
  - A redacted output is left out of `Output` and stays in `Result.outputs`.
  - A child with no outputs gives `{}`.
- [ ] T004 [P] `DispatchWorkflowCheckpointTests`: a waiting parent resumed after a restart gives the same `Output`
  (FR-012).
- [ ] T005 [P] Check that `Output` is capturable in the Design descriptor and the Studio output list (FR-005). Change
  nothing if it already shows. Update `EXTENSION_POINTS.md` only if it lists outputs.

## Phase 2: Converter rules (US2 to US4; FR-006 to FR-011)

- [ ] T006 Dispatch Workflow: `Lost("Result")` becomes `CarryOutput("Result", "Output")` (FR-006). Converter tests: a
  `Result` capture with `WaitForCompletion` literal true, literal false and bound to an expression gives an `Output`
  capture into the same variable, with no `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`.
- [ ] T007 Outcomes (FR-007, FR-009). Dispatch Workflow: `Done` goes to `Dispatched`, `Completed`, `Faulted` and
  `Cancelled`. Execute Workflow: `Done` goes to `Completed`, `Faulted` and `Cancelled`. Converter tests pin the emitted
  connections.
- [ ] T008 Make `SemanticsChanged` message and guidance rule data, with the current HTTP text as the default.
  `ParsedContent` tests stay unchanged.
- [ ] T009 Execute Workflow: `SemanticsChanged("Result")` with the FR-010 message and guidance. A converter test
  asserts the code, both ways forward, and the read mapping (`Result.Output.X` to `Output.X`, `WorkflowInstanceId` to
  `ChildWorkflowExecutionId`, status to outcome).
- [ ] T010 Implement the `ExecuteWorkflowAsDispatch` rewrite (`ActivityMap/Rewrites/`) and register it like the other
  rewrites. `WaitForCompletion` becomes `Structure`. The rewrite always emits literal `true` and raises
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
- [ ] T013 [P] Converter README (`extensions/Elsa3/src/Mapping/README.md`): add the FR-006 to FR-011 rows, the
  accepted difference for a faulted child's outputs, and Execute Workflow's always-wait.
- [ ] T014 Set the spec's status to `Implemented` in the implementation PR (not in a separate PR). Tell the S-15 session
  which adjusted-copy and ledger changes FR-013 allows. Do not edit the ledger here.
