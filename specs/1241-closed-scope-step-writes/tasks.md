# Tasks: A step writes its reusable body's own variables

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. `[P]` = can run in parallel (different files).

## Phase 1: Runtime row commit (US1, US2)

- [x] T001 Extract `ClosedScopeRowCommit` from `RuntimeOutputCaptureProjector.VersionRowAsync` with a per-commit row
  overlay; captures use it; existing row-capture tests stay green. *Done: `ClosedScopeRowCommit` (Activities.Runtime/Services).*
- [x] T002 Captures of one completion into the same body variable judge against the overlay (FR-007); test two captures
  into one row: two records with consecutive versions. *Done: `Two_captures_of_one_completion_into_one_variable_judge_in_order`.*
- [x] T003 `StepVariableSession.Stage`: remove the closed-scope refusal; intents carry `ClosedScopeBoundaryId`. Session
  tests: write, read-your-writes, type mismatch and transient faults in a closed-scope frame. *Done. Closed-scope frame values are typed `Any`, so the session checks a body write against the row's declared type (`RuntimeVisibleVariableFrames.RootVariableTypes`). Type mismatch is covered end to end in `GraphBodyStepWriteTests`; a transient body variable is not covered (a body row is never transient).*
- [x] T004 Session row writes: build the row from the stored row (driver, metadata, protection minimum) and judge it
  through `ClosedScopeRowCommit` with source `SessionChange`.
- [x] T005 `StepVariableCommit`: on suspension apply row intents; on completion skip row intents already applied; row
  records advance invocation bases and join its variable writes.
- [x] T006 Invoke and resume handlers: apply session row intents before the capture projection, pass the overlay, route
  a `Fault` session row lost update through `rowLostUpdateFault`.
- [x] T007 Run tests (end to end through a published graph activity): US1 scenarios 1–5, US2 scenarios 1–6, nested
  body, loop inside body, caller variable of the same name unchanged (SC-005). *Done in `GraphBodyStepWriteTests` (Activities.Behavioral), over hand-built placed boundaries, not through publication. US2 AS2 is covered on the resume path (warning and `Fault`); the digest exception under `Fault` is the shared commit rule and is not repeated. Not covered: a loop inside a body (a loop item stays read-only through the existing iteration-frame refusal); a sensitive body variable (a graph variable declares no protection of its own; the row write combines policies and hides the digest in `ClosedScopeRowCommit`, as a row capture does); an operator retry or re-execute of a faulted body step (it runs the step again through the same handlers).*

## Phase 2: Publish checks (US3)

- [x] T008 `IStepVariableWriteAnalyzer` in Validations.Core, implemented over `StepVariableWriteValidation`, registered
  in `WorkflowDesignValidationsFeature`; feature registration test; catalog entries. *Done; also enforced by `DataSelectionAnalyzerCompositionCheck` (exactly one scoped).*
- [x] T009 `StepVariableWriteValidation`: optional refused-literal-write reporting (VF-WRITE-002..004) for graph mode.
- [x] T010 `GraphActivityProvider.ValidateWriteOrder` (draft and compile): step-aware analyzer, reference errors,
  overlap warning, script-write refusals as graph diagnostics. Tests for US3 scenarios 1–5 and parity with a workflow
  draft (SC-004). *Done: `GraphStepWriteValidationTests` (mapping) and `StepVariableWriteAnalyzerTests` (parity with workflow draft validation).*

## Phase 3: Converter (US4)

- [x] T011 `Elsa3ValueFlowLowerer`: reusable scripts write variables; remove `ReusableVariableWriteGuidance` paths.
- [x] T012 Reusable input writes: dedicated refusal for `setVariable('<input>', v)`; generated input setter keeps its
  refusal. Tests.
- [x] T013 Convert the reusable refusal tests into conversion assertions; `ApplyDiscount` conversion + import run test
  (SC-001, SC-002). *Done. The `ApplyDiscount` conversion is pinned in `ApplyDiscount_converts_with_no_refusal`, and its converted script runs in a hand-built body in `A_body_script_changes_its_variable_and_the_next_step_and_the_output_read_it`; end to end, `ImportedReusableVariableWriteTests` imports `ApplyDiscount` from Elsa 3 JSON, publishes it and calls it twice (Elsa3 import integration tests).*

## Phase 4: Docs, Studio, close-out

- [x] T014 [P] Update `docs/runtime-variable-lifecycle.md`, `docs/reference/variable-write-order.md`, Validations
  `EXTENSION_POINTS.md`, `extensions/Elsa3/src/Mapping/README.md`, spec 1231 T035/T038 notes.
- [x] T015 [P] Studio: check in the browser that the new graph diagnostics show in the activity definition diagnostics
  panel with their locations. *See the PR evidence comment.*
- [x] T016 Regenerate maps if project references changed; build the touched solution filters; set spec status
  Implemented; update #4805.
