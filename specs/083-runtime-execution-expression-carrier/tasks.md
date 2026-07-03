---
description: "Task list for spec 083 — Runtime execution-time expression carrier"
---

# Tasks: Runtime Execution-Time Expression Carrier

**Input**: Design documents from `specs/083-runtime-execution-expression-carrier/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Governing decision**: [ADR 0030](../../docs/adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md)
**Architect decisions (2026-07-02)**: generic output accessors only (research.md R4); `RuntimeWorkflowExecutionContextTests` removal approved, objective re-based onto new carrier tests.

**Tests**: Included (the D4 guardrail + accessor/write-back end-to-end tests are core deliverables of this unit).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = safe feature enablement (P1), US2 = read identity/state (P1), US3 = persist variable mutations (P1)

## Path Conventions

Single project, domain-namespaced: `src/Elsa/...`, `tests/Elsa/...` at repo root.

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline before the refactor.

- [X] T001 Confirm a clean build/test baseline on branch `claude/serene-sinoussi-27deb0`: build the solution and run the existing runtime + JavaScript test projects (`tests/Elsa/Activities/Runtime/Tests`, `tests/Elsa/Workflows/Runtime/Tests`) and record the green baseline.
- [X] T002 Inventory all references to `IWorkflowExecutionContext` / `WorkflowExecutionContext` across `src/` and `tests/` (grep) so the retirement in Phase 3 is complete and nothing is missed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The carrier contract, its host implementation, and handler population. These block ALL user stories.

- [X] T003 Create the narrow marker `IExecutionExpressionState` in `src/Elsa/Workflows/Runtime/Core/Contracts/IExecutionExpressionState.cs` mirroring `IMaterializationExpressionState`, per contracts/execution-expression-carrier.md (identity: `WorkflowInstanceId`, `CorrelationId`, `WorkflowName`, `WorkflowDefinitionId`, `WorkflowDefinitionVersionId`, `WorkflowDefinitionVersion`; state: `WorkflowInputs`, `WorkflowVariables`, `ActivityOutputValues`). Runtime-only usings; no `Elsa.Workflows.Design.*`.
- [X] T004 Implement `IExecutionExpressionState` on `SimpleActivityExecutionContext` in `src/Elsa/Workflows/Runtime/Core/Services/SimpleActivityExecutionContext.cs`: add optional constructor parameters (correlationId, workflowName, workflowVariables, workflowInputs, activityOutputValues) defaulted so existing call sites keep compiling; expose carrier members; derive definition id/version from the existing `pinnedExecutable` (version resolution mirroring retired `WorkflowExecutionContext.ResolveWorkflowDefinitionVersion` — SystemMetadata key or artifact-version major). Keep variable read/write on `VariableScope`.
- [X] T005 In `WorkflowInvokeActivitySchedulerWorkHandler.InvokeActivityAsync` (`src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs`): load `WorkflowExecutionState` once up front (via `IWorkflowExecutionStateStore`), and pass correlation id + resolved instance name + the already-computed `workflowVariables`/`workflowInputs`/`activityOutputValues` projections into the `SimpleActivityExecutionContext` constructor (~:203-212).
- [X] T006 Refactor `BuildControlLeafWorkflowExecutionStateChangeAsync` to accept/reuse the workflow-execution state loaded in T005 instead of re-loading it (avoid a second `FindAsync`); preserve its existing null-check/throw behavior.

**Checkpoint**: Carrier exists and is populated on the execution path; nothing reads it yet.

---

## Phase 3: User Story 1 — Enable the JavaScript workflows runtime feature safely (P1)

**Goal**: Enabling `JavaScriptWorkflowsRuntimeFeature` no longer throws; every registered processor constructs and evaluation works.
**Independent test**: Enable the feature, resolve `IEnumerable<IScriptPreProcessor>`/`IScriptPostProcessor`, evaluate `1+1` — no resolution exception.

- [X] T007 [US1] Re-point `WorkflowInputFunctionsPreProcessor` (`src/Elsa/Workflows/Runtime/JavaScript/PreProcessors/WorkflowInputFunctionsPreProcessor.cs`): remove the `IWorkflowExecutionContext` ctor dep; cast the passed `IExpressionExecutionContext` to `IExecutionExpressionState`, return early (no-op) if not; register named input accessors (`get{Name}`) from `WorkflowInputs`; preserve the `IsContainedWithinCompositeActivity()` guard.
- [X] T008 [US1] Re-point `WorkflowFunctionsPreProcessor` (`.../WorkflowFunctionsPreProcessor.cs`): remove the ctor dep; source identity funcs (`getWorkflowInstanceId`, `getCorrelationId`, `getWorkflowInstanceName`, definition id/version/versionId, `getInput`, `getOutputFrom`, `getLastResult`) from the carrier; keep `setCorrelationId`/`setWorkflowInstanceName` routing through the execution context's existing control-leaf intent surface (`SetCorrelationId`/`SetInstanceName`); no-op when not an execution carrier.
- [X] T009 [US1] Re-point `VariableFunctionsPreProcessor` (`.../VariableFunctionsPreProcessor.cs`): keep `IOptions<FeatureOptions>`, remove `IWorkflowExecutionContext`; use `IScopedVariableProvider` for scope-based read/write (unchanged) and fall back to the carrier's `WorkflowVariables` (names + values) instead of `workflowExecution.GetVariables()/GetVariable()`; no-op when not an execution carrier.
- [X] T010 [US1] Re-point `ActivityOutputFunctionsPreProcessor` (`.../ActivityOutputFunctionsPreProcessor.cs`): remove the ctor dep; register generic output accessors (`getOutput(name)`, `getOutputFrom(activityIdOrName,name)`) from the carrier's `ActivityOutputValues`; register the pascalized `get{Output}From{Activity}` form only when an activity name is resolvable, else no-op (generic-accessors-only decision, research.md R4); no-op when not an execution carrier.
- [X] T011 [US1] Re-point `CopyVariablesToWorkflowContext` post-processor (`src/Elsa/Workflows/Runtime/JavaScript/PostProcessors/CopyVariablesToWorkflowContext.cs`): remove the ctor dep; derive input-name exclusion from the passed context (`expressionContext as IActivityExecutionContext` -> `.Activity` inputs/`SyntheticProperties`) instead of `IWorkflowExecutionContext.GetActivityContextForExpression`; keep copy-back via `expressionContext.SetVariable(...)` (routes to `VariableScope`); no-op when not an execution context.
- [X] T012 [US1] Delete `IWorkflowExecutionContext` (`src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutionContext.cs`) and concrete `WorkflowExecutionContext` (`src/Elsa/Workflows/Runtime/Core/WorkflowExecutionContext.cs`) after T007-T011 remove all `src/` references (confirm against T002 inventory). Ensure `JavaScriptWorkflowsRuntimeFeature.cs` still registers the same processor set.
- [X] T013 [US1] Add the D4 guardrail test `tests/Elsa/Activities/Runtime/Tests/JavaScriptRuntimeFeatureEvaluationTests.cs`: build a provider that enables `JavaScriptWorkflowsRuntimeFeature` (mirror `WriteLineVariableInputExpressionExecutionTests.BuildServiceProvider` wiring + `new JavaScriptWorkflowsRuntimeFeature().ConfigureServices(services)`); assert `IEnumerable<IScriptPreProcessor>` and `IEnumerable<IScriptPostProcessor>` resolve with all implementations constructed, and a smoke JS eval (e.g. `1+1`) returns the expected value with no resolution throw.

**Checkpoint**: Feature is safe to enable; the landmine is gone and guarded.

---

## Phase 4: User Story 2 — Read workflow identity and state from JavaScript (P1)

**Goal**: Execution-time scripts read identity, named accessors, inputs, and prior outputs.
**Independent test**: A `RunJavaScript` activity reads each surface and records it as output; assert values match seeded/pinned state.

- [X] T014 [US2] End-to-end test in `tests/Elsa/Activities/Runtime/Tests/RunJavaScriptExecutionAccessorsTests.cs`: execute a `RunJavaScript`-style activity through a `SimpleActivityExecutionContext` populated with identity + projections; assert `getWorkflowInstanceId()`, `getCorrelationId()`, `getWorkflowInstanceName()`, and definition id/version accessors return the pinned/seeded values.
- [X] T015 [P] [US2] Extend the accessor test (same file): assert the named accessor `getGreeting()` and `getInput("name")`/named input accessor return current variable/input values from the carrier.
- [X] T016 [P] [US2] Extend the accessor test (same file): seed a prior activity output projection and assert `getOutput(name)`/`getOutputFrom(activityIdOrName,name)` return the upstream value at execution time.
- [X] T017 [US2] Unit tests per re-pointed pre-processor under `tests/Elsa/Workflows/Runtime/Tests/` (or the JavaScript tests folder): cover the carrier-present branch and the carrier-absent no-op branch for `WorkflowFunctionsPreProcessor`, `WorkflowInputFunctionsPreProcessor`, `VariableFunctionsPreProcessor`, `ActivityOutputFunctionsPreProcessor` (§2.23.2 branch coverage).

**Checkpoint**: All read surfaces work end-to-end and are branch-covered.

---

## Phase 5: User Story 3 — Persist JavaScript variable mutations durably (P1)

**Goal**: Script variable mutations fold into the existing checkpoint-commit durable-value write-back; later activities observe them.
**Independent test**: One activity's script mutates a variable; a later activity reads it back; value survives a durable-store reload.

- [X] T018 [US3] Verify (and adjust only if needed) that script mutations via `setVariable`/`set{Name}`/`variables.x=` land in `VariableScope` and are captured by `BuildWorkflowScopeWriteBackChanges` and folded into `durableValueChanges` on completion (~:378-379) and onto suspend/child paths via `CombineDurableValueChanges` (~:292) — with NO second persistence route added.
- [X] T019 [US3] End-to-end test in `tests/Elsa/Activities/Runtime/Tests/` (new or extend `SetVariableDurabilityExecutionTests`-style coverage): one script activity sets a variable (test all three mutation forms), a later activity reads `variables.*` and observes the mutated value; assert persistence via durable-store reload.
- [X] T020 [P] [US3] Test the write-back fold assertions: (a) a read-only script activity produces zero durable-value write-back changes (dirty-tracking, SC-004); (b) the variable change appears within the activity's committed durable-value change set, not out-of-band.

**Checkpoint**: Variable write-back is durable, atomic with the checkpoint, and dirty-tracked.

---

## Phase 6: Polish & Cross-Cutting

- [X] T021 Remove `RuntimeWorkflowExecutionContextTests` (`tests/Elsa/Workflows/Runtime/Tests/RuntimeWorkflowExecutionContextTests.cs`) and any test helpers that `new` `WorkflowExecutionContext` (per T002 inventory); confirm the objective is covered by Phase 4/5 carrier tests (architect-approved, recorded in plan Complexity Tracking).
- [X] T022 [P] Update `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` to document the execution-time expression carrier (`IExecutionExpressionState`) and the JavaScript `IScriptPreProcessor`/`IScriptPostProcessor` surface it feeds; note the parameter-threaded, Design-free invariant.
- [X] T023 [P] Update spec 064 status linkage (`specs/064-runtime-workflow-execution-context/spec.md`) to point at spec 083 as the implementing unit; update the Runtime Execution Seam program goal (`docs/program-goals/runtime-execution-seam.md`) objective 13 to record spec 083 under the bucket.
- [X] T024 Refresh affected generated maps: check `docs/maps/manifest.json` and regenerate the maps touching the runtime JavaScript feature / expression seam (use the refresh-generated-maps skill).
- [X] T025 Final gate pass: full build; run runtime + JavaScript test projects; run the architecture/structural dependency check confirming no `Elsa.Workflows.Runtime.* -> Elsa.Workflows.Design.*` dependency and no DI registration of `IWorkflowExecutionContext` remains (§E2.2/§E2.6, SC-005).

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** -> **Phase 2 (Foundational)** -> **Phase 3 (US1)** -> **Phase 4 (US2)** / **Phase 5 (US3)** -> **Phase 6 (Polish)**.
- US2 and US3 both depend on US1 (processors re-pointed + feature safe). Within US1, T007-T011 can proceed in parallel per file, then T012 (deletion) after all references removed, then T013 (guardrail).
- The write-back fold (US3) depends on the carrier population (Phase 2) and variable processor re-point (T009/T011).

## Parallel Opportunities

- T007, T008, T009, T010, T011 touch separate processor files — parallelizable, but all must complete before T012.
- T015, T016 (accessor test extensions), T020 (write-back assertions), T022, T023 are `[P]` where they touch distinct files.

## Implementation Strategy

- **MVP = US1**: re-point processors + retire `IWorkflowExecutionContext` + guardrail. This alone removes the landmine and makes the feature safe (SC-001).
- **US2 + US3** deliver the restored product surfaces (identity/named/output reads; durable variable write-back). Ship after US1 is green.
- Keep every existing materialization test passing throughout (SC-006) — the execution processors no-op on materialization contexts.
