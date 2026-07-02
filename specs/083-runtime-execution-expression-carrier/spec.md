# Feature Specification: Runtime Execution-Time Expression Carrier

**Feature Branch**: `claude/serene-sinoussi-27deb0`

**Created**: 2026-07-02

**Status**: Draft

**Input**: Implement [ADR 0030](../../docs/adr/0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md). Introduce a live execution-time expression carrier (a narrow marker interface mirroring `IMaterializationExpressionState`) that runtime JavaScript pre/post-processors read via the passed `IExpressionExecutionContext` parameter, never via dependency injection. Re-point the five dead `IWorkflowExecutionContext` processors onto the carrier and strip their constructor dependencies; populate the carrier from the scheduler work handler; keep all four accessor surfaces (execution-time identity functions, named pascalized accessors, execution-time activity-output accessors, and JavaScript variable write-back); fold JavaScript variable write-back into the existing durable-value write-back at the checkpoint-commit boundary; retire `IWorkflowExecutionContext` as a DI dependency; and add a guardrail test that enables the JavaScript workflows runtime feature, resolves every registered script pre-processor, and runs an end-to-end smoke evaluation. Re-base [spec 064](../064-runtime-workflow-execution-context/spec.md) FR-001…FR-005 onto the carrier.

> **Scope note.** This specification is bounded by ADR 0030 (decisions D1–D4, questions Q1–Q3) and the source baseline in the [runtime expression-context source reconciliation](../../docs/reports/runtime-expression-context-source-reconciliation.md). The design is settled; this spec captures the required behavior and acceptance criteria, not new design decisions. It carries [spec 064](../064-runtime-workflow-execution-context/spec.md)'s intent forward while superseding its mechanism (no DI-registered `IWorkflowExecutionContext`).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enable the JavaScript workflows runtime feature safely (Priority: P1)

A host operator composing an Elsa server enables the JavaScript workflows runtime feature so workflow authors can use JavaScript expressions and *Run JavaScript* activities. When any workflow first evaluates a JavaScript expression, every registered script pre-processor and post-processor must construct and the evaluation must succeed.

**Why this priority**: Today, enabling the feature is an unguarded landmine. Five registered processors take a constructor dependency (`IWorkflowExecutionContext`) that is registered nowhere in production. Because the evaluation pipeline resolves the whole `IEnumerable<IScriptPreProcessor>`, a single missing dependency throws for the entire set — taking down even the one working processor — on the first script evaluation. Making feature-enablement safe is the foundational fix; nothing else in this feature is observable until it holds.

**Independent Test**: Enable the JavaScript workflows runtime feature in a host/service collection, resolve `IEnumerable<IScriptPreProcessor>` and `IEnumerable<IScriptPostProcessor>`, and evaluate a trivial script (e.g. `1 + 1`). All processors construct and the evaluation returns the expected value with no resolution exception.

**Acceptance Scenarios**:

1. **Given** the JavaScript workflows runtime feature is enabled, **When** the expression pipeline resolves all registered script pre/post-processors, **Then** every processor is constructed without a missing-dependency exception.
2. **Given** the JavaScript workflows runtime feature is enabled, **When** a workflow evaluates a JavaScript expression for the first time, **Then** the expression evaluates end-to-end and returns its value.
3. **Given** no live workflow-execution service is registered in DI, **When** the feature is enabled and a script is evaluated, **Then** evaluation still succeeds (no processor depends on a DI-registered live execution context).

---

### User Story 2 - Read workflow identity and state from JavaScript at execution time (Priority: P1)

A workflow author writes a *Run JavaScript* activity (or a JavaScript expression evaluated during activity execution) that reads workflow identity and workflow state — the workflow instance id, correlation id, workflow name, workflow inputs, workflow variables, and prior activity outputs — after inputs have been seeded.

**Why this priority**: These are the confirmed-required accessor surfaces (ADR 0030 D3) that are dead today. A *Run JavaScript* activity that calls `getWorkflowInstanceId()`, `getCorrelationId()`, a named accessor like `getGreeting()`, or an output accessor currently cannot work because the processors that register those functions never construct. Restoring them is the primary product value of this feature.

**Independent Test**: Execute a workflow whose *Run JavaScript* activity reads each accessor surface and records the result as output; assert the recorded values match the seeded/pinned workflow state.

**Acceptance Scenarios**:

1. **Given** a running workflow pinned to an executable artifact, **When** a *Run JavaScript* activity calls `getWorkflowInstanceId()`, `getCorrelationId()`, and the workflow-name accessor, **Then** the returned values match the workflow execution's identity and the pinned executable identity, without loading authored workflow models.
2. **Given** a workflow with a variable named `greeting`, **When** script calls the named accessor `getGreeting()`, **Then** it returns the current value of the `greeting` variable.
3. **Given** a workflow with seeded inputs, **When** script calls the input accessors, **Then** it returns the current input values.
4. **Given** an upstream activity produced an output, **When** a later activity's script reads the execution-time activity-output accessor for that output, **Then** it returns the upstream output value.

---

### User Story 3 - Persist JavaScript variable mutations durably (Priority: P1)

A workflow author writes a *Run JavaScript* activity that mutates a workflow variable (via `setVariable(...)`, a named setter like `setStatus(...)`, or a direct `variables.status = ...` assignment). After the activity completes, later activities and resumed instances observe the mutated value.

**Why this priority**: JavaScript variable write-back is the highest-risk surface (ADR 0030 D3/§Consequences). Without it, variable-driven loops and script-computed state silently fail to persist. It must fold into the *existing* durable-value write-back at the checkpoint-commit boundary — not a second persistence route — so that it commits atomically with the activity's checkpoint.

**Independent Test**: Execute a workflow where one activity's script mutates a workflow variable and a later activity reads it back; assert the later activity observes the mutated value, and that the value survives a reload from the durable store.

**Acceptance Scenarios**:

1. **Given** a workflow variable `status`, **When** a *Run JavaScript* activity sets it via `setVariable("status", "approved")` and completes, **Then** a later activity reading `variables.status` observes `"approved"`.
2. **Given** a workflow variable `status`, **When** a script assigns `variables.status = "approved"` directly and the activity completes, **Then** the mutation is persisted through the same durable-value write-back used by non-script variable mutations.
3. **Given** an activity that mutates a variable, **When** the activity checkpoint commits, **Then** the variable write-back is part of that commit's durable-value change set (no out-of-band second write).
4. **Given** a read-only script activity over a workflow that declares variables, **When** the activity completes, **Then** no spurious variable write-back change is produced (dirty-tracked).

---

### Edge Cases

- **Missing runtime value**: When script requests a workflow input, variable, or output that does not exist in the carrier, the accessor behaves deterministically (returns null/undefined for value accessors, or fails with a deterministic `InvalidOperationException` where the superseded 064 contract required it — see FR-013), never `NotImplementedException`.
- **Composite-activity scope**: When the current activity is contained within a composite activity, workflow-input accessors do not shadow composite-activity input accessors (preserving the existing `IsContainedWithinCompositeActivity` guard behavior).
- **Scope chain present vs. absent**: When a container-scope chain is visible (ADR 0027), name-based variable read/write resolves through it with nearest-scope shadowing; otherwise it falls back to workflow-scope behavior.
- **Input-name collision on write-back**: When a script variable name collides with an activity/workflow input name, the direct-assignment write-back excludes input-named entries so it does not overwrite activity inputs.
- **Suspend / child-scheduling paths**: When an activity suspends (creates a bookmark) or schedules children after mutating a variable via script, the write-back rides the same suspend/child-scheduling durable-value fold the existing workflow-scope write-back uses, so it commits with the continuation.
- **Materialization-time evaluation unaffected**: Expressions evaluated at input-materialization time continue to resolve through the existing `IMaterializationExpressionState` carrier; the new execution-time carrier does not change that path.

## Requirements *(mandatory)*

### Functional Requirements

**Carrier (live execution-time expression state)**

- **FR-001**: The runtime MUST expose execution-time workflow state to expression pre/post-processors through a narrow, purpose-named marker interface (mirroring `IMaterializationExpressionState`), obtained by casting the `IExpressionExecutionContext` parameter passed to the processor — NOT through dependency injection.
- **FR-002**: The carrier MUST be a narrow marker interface, NOT a general transient-properties bag on `IExpressionExecutionContext` (ADR 0030 Q3). The two narrow carriers (materialization-time and execution-time) coexist.
- **FR-003**: The carrier MUST be built and populated by the scheduler work handler (`WorkflowInvokeActivitySchedulerWorkHandler`) at execution time, from state the handler already assembles (projected workflow variables, workflow inputs, and activity outputs) plus workflow identity (`WorkflowExecutionState` and the pinned executable identity).
- **FR-004**: The carrier MUST surface, for execution-time expressions: workflow instance id, correlation id, workflow name, workflow definition id/version(-id), workflow inputs, workflow variables, and prior activity outputs.

**Re-point the five processors**

- **FR-005**: `WorkflowInputFunctionsPreProcessor`, `WorkflowFunctionsPreProcessor`, `VariableFunctionsPreProcessor`, `ActivityOutputFunctionsPreProcessor`, and `CopyVariablesToWorkflowContext` MUST read execution-time state from the passed carrier and MUST NOT take `IWorkflowExecutionContext` (or any live workflow-execution service) as a constructor dependency.
- **FR-006**: Each re-pointed processor MUST be a no-op when the passed `IExpressionExecutionContext` is not the execution-time carrier (mirroring how `MaterializationAccessorsPreProcessor` returns early for non-materialization contexts), so it is safe to register globally and does not interfere with materialization-time evaluation.

**Accessor surfaces (keep all four — ADR 0030 D3)**

- **FR-007**: Execution-time identity functions (`getWorkflowInstanceId`, `getCorrelationId`, workflow-name getter, and the definition-id/version getters) MUST resolve from the carrier's identity values.
- **FR-008**: Named pascalized variable and input accessors (e.g. `getGreeting()` for a variable named `greeting`, and the named input accessor) MUST be generated per declared name and resolve/assign through the carrier.
- **FR-009**: Execution-time activity-output accessors (e.g. `get{Output}From{Activity}` and the generic output getter) MUST read prior activity outputs from the carrier.
- **FR-010**: JavaScript-side variable write-back MUST be supported for all three mutation forms: `setVariable(...)`, named setters (`set{Name}(...)`), and direct `variables.*` assignment copied back after evaluation.
- **FR-011**: Existing correlation-id / instance-name assignment from script (`setCorrelationId`, `setWorkflowInstanceName`) MUST continue to route through the runtime's existing control-leaf intent path (correlation/name changes fold into the activity-completed workflow-execution state change), consistent with current behavior.

**Write-back fold**

- **FR-012**: JavaScript variable mutations MUST persist by folding into the EXISTING durable-value write-back on the checkpoint-commit boundary (the same path that persists non-script workflow-scope variable mutations). The feature MUST NOT introduce a second persistence route.
- **FR-013**: Write-back MUST be dirty-tracked so a read-only script activity produces no durable-value change, and MUST commit atomically with the activity's checkpoint on the completion path, and ride the existing suspend/child-scheduling durable-value fold on those paths.

**Retirement & guardrail**

- **FR-014**: `IWorkflowExecutionContext` MUST be retired as a DI dependency and MUST NOT be a registered service. The concrete `WorkflowExecutionContext` type may be renamed/reworked into the carrier or removed, but MUST NOT return as a registered service.
- **FR-015**: A guardrail test MUST enable the JavaScript workflows runtime feature, resolve `IEnumerable<IScriptPreProcessor>` (and post-processors), and run a smoke JavaScript evaluation end-to-end, asserting every registered processor constructs and evaluation works.
- **FR-016**: The runtime execution-time carrier and all re-pointed processors MUST NOT introduce any dependency on `Elsa.Workflows.Design.*` (constitution §E2.2 / §E2.6 — carrier is Runtime-owned and Design-free).

**Spec 064 re-base**

- **FR-017**: Spec 064's FR-001…FR-005 intent MUST be satisfied on the carrier: (064 FR-001) workflow execution identity from `WorkflowExecutionState`; (064 FR-002) definition identity from `WorkflowExecutionState.PinnedExecutable`; (064 FR-003) correlation id and name readable at execution time without mutating authored models; (064 FR-004) workflow inputs exposed as runtime values; (064 FR-005) variables readable, listable, and updatable — all via the parameter-threaded carrier rather than a DI-registered mutable context.

### Key Entities *(include if feature involves data)*

- **Execution-time expression carrier**: A narrow marker interface exposing execution-time workflow state (identity, inputs, variables, prior outputs) to expression pre/post-processors. Implemented by the execution-time `IExpressionExecutionContext` and populated by the scheduler work handler. Analogous to `IMaterializationExpressionState` but for the post-seed, mid-execution evaluation point.
- **Re-pointed script processors**: The five previously-dead pre/post-processors, now dependency-free and reading from the carrier.
- **Durable-value write-back change set**: The existing set of `DurableValueState` changes folded into the activity checkpoint commit; JavaScript variable mutations join this set rather than persisting separately.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the JavaScript workflows runtime feature enabled, resolving all registered script pre/post-processors and evaluating a script succeeds in 100% of cases (0 resolution exceptions) — verified by the guardrail test that does not exist today.
- **SC-002**: All four execution-time accessor surfaces (identity, named accessors, activity outputs, variable write-back) are exercised by end-to-end tests and return/persist the correct values.
- **SC-003**: JavaScript variable mutations are observable by later activities and survive a durable-store reload in 100% of tested mutation forms (`setVariable`, named setter, direct assignment).
- **SC-004**: A read-only script activity produces zero durable-value write-back changes (dirty-tracking holds), and variable write-back appears only within the activity's checkpoint commit set (no second persistence route) — verified by inspecting the committed change set in tests.
- **SC-005**: No production DI registration of `IWorkflowExecutionContext` remains; a structural/architecture check confirms the runtime carrier and processors carry no `Elsa.Workflows.Design.*` dependency.
- **SC-006**: Materialization-time expression evaluation continues to pass all existing tests unchanged (no regression to the working materialization path).

## Assumptions

- The execution-time `IExpressionExecutionContext` used by *Run JavaScript* and execution-time expression evaluation is `SimpleActivityExecutionContext` (it already exposes `ExpressionExecutionContext => this` and implements `IScopedVariableProvider`); it is the natural home for the execution-time carrier marker. Final type naming is an implementation detail settled in planning.
- Variable write-back reuses the existing `VariableScope` → `BuildWorkflowScopeWriteBackChanges` durable-value fold in `WorkflowInvokeActivitySchedulerWorkHandler`; name-based variable set already routes through `IScopedVariableProvider`/`VariableScope`, so script mutations land in the same write-back set with no new persistence path.
- Workflow identity (correlation id, name) that is not already held by the execution-time context is sourced by the handler from `WorkflowExecutionState`; the handler may load workflow-execution state once on the execution path to populate the carrier (marginal projection cost, no new I/O pattern beyond what the control-leaf path already does).
- The `WorkflowFunctionNames` constants and the four Design-side JavaScript declaration contributors already describe these functions to the editor; this feature wires the runtime side to match, and does not change the authored declaration surface.
- This unit is independent of ADR 0029 (pipeline wiring); the carrier is populated by the scheduler work handler regardless of whether execution is later routed through the activity pipeline.
- Spec 064 remains superseded-in-mechanism; its status linkage is updated to point at this spec as the implementing unit.
