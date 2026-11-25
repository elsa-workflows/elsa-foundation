# Tasks: A step writes its reusable body's own variables

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Tests are xunit only. `[P]` = can run in parallel (different files).

## Phase 1: Runtime row commit (US1, US2)

- [ ] T001 Extract `ClosedScopeRowCommit` from `RuntimeOutputCaptureProjector.VersionRowAsync` with a per-commit row
  overlay; captures use it; existing row-capture tests stay green.
- [ ] T002 Captures of one completion into the same body variable judge against the overlay (FR-007); test two captures
  into one row: two records with consecutive versions.
- [ ] T003 `StepVariableSession.Stage`: remove the closed-scope refusal; intents carry `ClosedScopeBoundaryId`. Session
  tests: write, read-your-writes, type mismatch and transient faults in a closed-scope frame.
- [ ] T004 Session row writes: build the row from the stored row (driver, metadata, protection minimum) and judge it
  through `ClosedScopeRowCommit` with source `SessionChange`.
- [ ] T005 `StepVariableCommit`: on suspension apply row intents; on completion skip row intents already applied; row
  records advance invocation bases and join its variable writes.
- [ ] T006 Invoke and resume handlers: apply session row intents before the capture projection, pass the overlay, route
  a `Fault` session row lost update through `rowLostUpdateFault`.
- [ ] T007 Run tests (end to end through a published graph activity): US1 scenarios 1–5, US2 scenarios 1–6, nested
  body, loop inside body, caller variable of the same name unchanged (SC-005).

## Phase 2: Publish checks (US3)

- [ ] T008 `IStepVariableWriteAnalyzer` in Validations.Core, implemented over `StepVariableWriteValidation`, registered
  in `WorkflowDesignValidationsFeature`; feature registration test; catalog entries.
- [ ] T009 `StepVariableWriteValidation`: optional refused-literal-write reporting (VF-WRITE-002..004) for graph mode.
- [ ] T010 `GraphActivityProvider.ValidateWriteOrder` (draft and compile): step-aware analyzer, reference errors,
  overlap warning, script-write refusals as graph diagnostics. Tests for US3 scenarios 1–5 and parity with a workflow
  draft (SC-004).

## Phase 3: Converter (US4)

- [ ] T011 `Elsa3ValueFlowLowerer`: reusable scripts write variables; remove `ReusableVariableWriteGuidance` paths.
- [ ] T012 Reusable input writes: dedicated refusal for `setVariable('<input>', v)`; generated input setter keeps its
  refusal. Tests.
- [ ] T013 Convert the reusable refusal tests into conversion assertions; `ApplyDiscount` conversion + import run test
  (SC-001, SC-002).

## Phase 4: Docs, Studio, close-out

- [ ] T014 [P] Update `docs/runtime-variable-lifecycle.md`, `docs/reference/variable-write-order.md`, Validations
  `EXTENSION_POINTS.md`, `extensions/Elsa3/src/Mapping/README.md`, spec 1231 T035/T038 notes.
- [ ] T015 [P] Studio: check in the browser that the new graph diagnostics show in the activity definition diagnostics
  panel with their locations.
- [ ] T016 Regenerate maps if project references changed; build the touched solution filters; set spec status
  Implemented; update #4805.
