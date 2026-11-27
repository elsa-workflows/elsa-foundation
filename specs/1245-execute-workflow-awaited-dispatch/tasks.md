# Tasks: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. `[P]` means the task can run in parallel with others (it touches different files).

## Phase 1: Runtime `Output` (US1; FR-001 to FR-005)

- [x] T001 `DispatchWorkflowActivityResult`: add `Output`, with the FR-003 contract description (the declared
  loss), (`JsonElement?`,
  `[Output(Key = "Output", Path = "output", IsRequired = false)]`), left out of the serialized result when null (plan
  D-1). Contract test in `DispatchWorkflowContractTests`: the output is declared, it is optional, and the descriptor
  lists it.
- [x] T001a Remove the `Result` output (FR-004, FR-005).
  - `DispatchWorkflowActivityResult.Result` becomes `Terminal`, which is not an output.
    `IRuntimeChildControlCompletion` reads `Terminal?.Control`.
  - `DispatchWorkflowResult` loses `Outputs`, and `DispatchWorkflowOutput` is deleted. The enricher stops calling `ReadAsync` and calls
    `ReadValuesAsync` (T003).
  - Contract test: the outputs are exactly `ChildWorkflowExecutionId`, `Output` and `Status`.
  - Add `Status` (FR-004a). Run-time tests: it equals the selected outcome for `Dispatched`, `Completed`, `Faulted`,
    `Cancelled` and `DispatchFailed`.
  - Update the consumers in plan Runtime step 6:
    - the contract surface baseline;
    - the Dispatch Workflow `EXTENSION_POINTS.md` and `README.md`;
    - the dispatch tests that build or read `Result`;
    - the `DispatchWorkflowDrive` comment;
    - the regenerated parity report `elsa4-surface.json`.
  - Add the supersession notes to specs 096, 099 and 100. Each names its superseded `spec.md`, contract and data-model
    parts (FR-004).
  - Close #5851 when this lands.
- [x] T002 Test first, in `DispatchWorkflowEndToEndTests`, on the real `WorkflowOutputSource` with default diagnostics
  settings (FR-002, plan D-1):
  - Fire-and-forget gives no `Output`, and a variable that captures it and holds `"before"` still holds `"before"`.
  - Faulted, cancelled and dispatch-failed children give `Output` `{}`. A captured variable that was never set reads
    `undefined` for a member after them, and does not throw.
  > **Implementation note (PR #5999):** covered at the activity result: no `output` key on Dispatched, `{}` on Faulted, Cancelled and DispatchFailed. The variable-level checks (`"before"` kept, `undefined` member read) rely on the generic capture projector and are not run here; the import integration runs the faulted `{}` read end to end.
- [x] T003 Policy-free read of a child's committed workflow outputs (plan D-2): `IWorkflowOutputSource.ReadValuesAsync`,
  returning `WorkflowOutputValues`, implemented in `WorkflowOutputSource`. Update both `EXTENSION_POINTS.md` entries and
  `PublicAPI.Unshipped.txt` (the member and the record), and
  give the test stubs (`WorkflowControlBoundaryTests.NoOutputs`, `WorkflowDispatchCompletionEnricherTests.StubOutputSource` and
  `ThrowingOutputSource`, which must throw from `ReadValuesAsync` too) the member. It selects the same outputs as
  `RuntimeWorkflowOutputStateProjection`, without `IRuntimePayloadCapturePolicy`. It leaves out values whose
  `DurableValuePolicies.Read` is protected (sensitive, encryption, redaction mode, retention) and values not stored
  inline (`!InlineValue.HasValue || DurableValuePolicies.Read(row).Storage != Inline`). It returns the left-out names.
  The encryption case and the null row with an External minimum are each tested with an explicit null. Add unit tests beside the existing projection tests, one per exclusion. Fix both stale comments in
  `RuntimeWorkflowOutputStateProjection`: "nothing writes it yet" (the sensitive marker) and "nothing writes
  externalized workflow outputs yet". Set Output writes both.
- [x] T003a `WorkflowDispatchCompletionEnricher` sets `WorkflowDispatchParentResumePayload.Output` for `Completed` from
  T003.
  - The payload carries `WithheldOutputs` (names only, sorted ordinally). Both new members are constructor parameters:
    null unless `Completed`, and `WithheldOutputs` is empty when nothing is left out. Payload `Output` stays
    `JsonElement?`.
  - The payload constructor rejects `Output` and `WithheldOutputs` on any other status.
  - Test: several withheld names arrive in reverse store order, and the re-enriched intent is still equivalent.
  - `WorkflowDispatchDeliveryFailureProjector` passes null, and `DispatchWorkflowResumeGuard` reads the member.
  - Update the "payload-safe" doc comment.
  - Enricher tests: the value is replay-stable; a conflicting re-enrichment fails closed; a payload with `Output` on
    `Faulted` is rejected; the terminal result keeps its status, diagnostics and control.
- [x] T003c Add `Warnings` (`IExecutionWarningSink?`) to `ActivityResumeContext<TState, TTrigger>`, with its
  `Activities/Runtime/Core/PublicAPI.Unshipped.txt` entries, copied from the
  attempt. Test that a warning reported in `ResumeAsync` is recorded on the activity execution. Update
  `EXTENSION_POINTS.md` if it lists context members.
- [x] T003b `DispatchWorkflow.ResumeAsync` copies `payload.Output` into the activity result for `Completed`. It sets
  `Output` to `{}` for `Faulted`, `Cancelled` and `DispatchFailed`, which is what T002 tests for each status. It also
  reports
  `dispatch.child-output-withheld` (Warning, `Details.outputs`) through T003c when `WithheldOutputs` is not empty. End-to-end
  tests:
  - A child with output `Decision = "approved"` gives `{ "Decision": "approved" }`.
  - A 1,000-character `Summary` and an output named `token` arrive unchanged.
  - Outputs that are sensitive, encryption-required, redaction-moded or retention-governed are left out. The dispatch
    reports one `dispatch.child-output-withheld` warning whose `outputs` detail lists their names in ordinal order. The
    warning contains no value.
  - Two declared outputs with one set give one property.
  - A child with no outputs gives `{}`.
- [x] T004 [P] `DispatchWorkflowCheckpointTests`: a waiting parent resumed after a restart gives the same `Output`
  (FR-012).
- [x] T005 [P] Check that `Output` is capturable, and that `Result` is gone, in the Design descriptor and the Studio
  output list (FR-005). Change nothing there if they follow the contract.

## Phase 2: Converter rules (US2 to US4; FR-006 to FR-011)

- [x] T006 Dispatch Workflow: `Lost("Result")` becomes `CarryOutput("Result", "Output")` (FR-006). Converter tests: a
  `Result` capture with `WaitForCompletion` literal true, literal false and bound to an expression gives an `Output`
  capture into the same variable, with no `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`.
- [x] T007 Outcomes (FR-007, FR-009). Dispatch Workflow: `Done` goes to `Dispatched`, `Completed` and `Faulted`.
  Execute Workflow: `Done` goes to `Completed` and `Faulted`. `Cancelled` stays unmapped. Converter tests pin the
  emitted connections.
- [x] T008 Give `Elsa3MemberRule` optional per-rule message and guidance, with today's text as the default. Existing
  lost-output tests stay unchanged.
- [x] T009 Execute Workflow wrapped result (FR-010, plan D-3).
  - `CarryOutput("Result", "Output")` with the wrapped-result marker.
  - Lowering rewrites every read of the captured variable: JavaScript member paths, Liquid member paths, a bare
    `getV()`, and a direct binding of `V`.
  - Converter tests: one per FR-010 table row in JavaScript, Liquid `.Output` and `.Output.X`, and one per refusal
    (another writer, an initial value, an unknown member, another Liquid read, a Liquid `Output` root read of the node's `Result`, a cross-frame read, an out-of-order
    read, a computed-name read in scope of `V`). Cover the literal `getVariable('V')` form with one member-path read and
    one whole-value read.
  - One refusal test per writer kind: another capture, Set Variable, an initial value (absent, `null` and `""` do not
    count), `setV`, a literal `setVariable('V', …)`, a member assignment, and a computed-name write.
  - Tests for the literal bracket form and for optional chaining. Add one test with an Execute Workflow and a Dispatch Workflow that
    both capture `Result`: each variable's reads bind to its own node.
  - One publish test: a `.SubStatus` read on a path where the dispatch may not have run is refused with
    `ValueFlow/UnavailableProducer`.
  - Run-time tests: US3 scenario 4 with a child that completes and with one that faults. The whole-value read is
    checked against the Elsa 3 key set.
  > **Implementation note (PR #5999):** the publish test for `ValueFlow/UnavailableProducer` is not written (the mapping suite cannot publish); run-time rows are covered in the import integration for Output.X, Status, SubStatus, WorkflowInstanceId and the whole value.
- [x] T010 Implement the `ExecuteWorkflowAsDispatch` rewrite (`ActivityMap/Rewrites/`) and register it in
  `Elsa3MappingFeature`, with a feature-registration test. `WaitForCompletion` becomes
  `Structure("WaitForCompletion", Elsa3MemberKind.Input)`, and the rewrite emits it with `SetProperty`, so
  `RemoveConsumedStructure` keeps it. The rewrite always emits literal `true` and raises
  `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` once. The warning adds the suspended-child sentence unless the Elsa 3 value
  is literal true (FR-008).
  - Tests: an unset value, literal true, literal false, a bound value and an explicit null. In each case the output is
    literal true and there is exactly one warning. The sentence appears in all of them except literal true.
  - Update `A_null_ExecuteWorkflow_wait_keeps_spec_236s_mapping` and `Elsa3ConversionDiagnosticsTests` to the new
    rule.
- [x] T011 [P] Update `Elsa3ActivityMapCoverageTests` and the `elsa3-activity-surface.json` fixture to the new rules.
  Find the tests that pinned the old rules and update them (SC-004). List them in the PR.

## Phase 3: Integration and records

- [x] T012 Import integration (`DispatchWorkflowCallTests` with `dispatch-caller.json`): add an awaited Elsa 3 dispatch
  that captures `Result` into an `Object` variable and one into an `ObjectDictionary` variable, and reads a child output
  by name. Import, publish and run it on the import host, then assert the value (FR-001 capture at publish, FR-006,
  US2 scenario 2). Run Flowchart-parent cases at run time:
  - a faulted child runs the node on Elsa 3's `Done`, for Execute Workflow and for an awaited Dispatch Workflow
    (FR-007, FR-009, US3 scenario 3, US4 scenario 2);
  - a cancelled child ends the branch (FR-011).

  Keep one Sequence-parent case as a regression check that a faulted child still runs the next step.
  > **Implementation note (PR #5999):** the cancelled-child run-time case is not written (it needs a live child cancelled through the host); `Cancelled` routing is pinned by converter tests and `Output` `{}` on Cancelled by a dispatch end-to-end test.
- [x] T013 [P] Converter README (`extensions/Elsa3/src/Mapping/README.md`): add the FR-006 to FR-011 rows, the six
  accepted differences (a faulted child's outputs, a cancelled child, a failed child start, `ParentInstanceId`,
  output-name case, the suspended-child shape) with their Flowchart and Sequence behaviour, and Execute Workflow's always-wait.
- [x] T013a [P] Amend spec 236's `member-map.md` rows for `DispatchWorkflow.Result`, the `Done` outcome and
  `ExecuteWorkflow` in place, and the null-member row for `ExecuteWorkflow.WaitForCompletion`, citing spec 1245
  (FR-011a). The `ExecuteWorkflow` row also corrects the default: Elsa 3
  ran an unset `WaitForCompletion` as false, where today's `DefaultFlip(…, "true")` assumes true.
- [x] T014 Set the spec's status to `Implemented` in the implementation PR (not in a separate PR). Tell the S-15 session
  which adjusted-copy and ledger changes FR-013 allows. Do not edit the ledger here.
