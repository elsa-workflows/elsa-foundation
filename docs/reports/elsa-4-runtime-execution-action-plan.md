# Elsa 4 Runtime Execution Action Plan

Status: pre-Speckit action plan derived from locked runtime execution brainstorm decisions. This is not a ratified Speckit spec or implementation task list.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Decision source: [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md).

Source evidence: [Elsa Core runtime execution layer analysis](elsa-core-runtime-execution-layer-analysis.md).

Related context: [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md).

Addendum queue: [Elsa 4 runtime execution addendum topics](elsa-4-runtime-execution-addendum-topics.md).

## Purpose

Turn the locked execution-layer brainstorm decisions into an actionable planning sequence for Elsa 4. The plan is intentionally staged so implementation starts with boundaries and contracts before importing or rewriting Elsa 3 behavior.

## Planning Frame

Build Elsa 4's execution layer from clean contracts, using Elsa 3 as behavioral evidence.

Preserve from Elsa 3:

- Executable workflow lifecycle: start, run, suspend, resume, complete, fault, cancel.
- Separate workflow and activity execution concerns.
- Scheduled work items.
- Per-activity execution identity.
- Durable bookmarks.
- Checkpoint/commit boundaries.
- Diagnostics separate from runtime continuation state.
- Operational recovery and post-commit outbox reliability.

Do not preserve as-is:

- Runtime dependence on Design/Management workflow definitions during execution.
- Monolithic `WorkflowState`.
- Durable callback method names in bookmarks.
- Activity output lookup by authored activity ID as a durable data source.
- History/log records as runtime continuation state.
- Transparent live resume of arbitrary Elsa 3 workflow instances.

## Recommended Speckit Starting Scope

Start with one Speckit unit:

```text
Runtime executable artifact and execution state contract
```

This first unit should define the minimum runtime-owned contracts that later scheduler, bookmark, pipeline, and persistence work can build on.

In scope:

- Runtime-owned executable artifact boundary and identity.
- Exact artifact snapshot pinning for workflow executions.
- Runtime-owned executable node identity.
- `WorkflowExecutionState` contract.
- `ActivityExecution` / `ActivityExecutionState` contract.
- `SchedulerState` minimal contract.
- `DurableValueState` reference boundary, reusing serialization/value decisions.
- Checkpoint contract names and default persistence policy hooks.
- Structural dependency rule: runtime execution must not load Design-owned authored documents at execution time.

Out of scope for the first unit:

- Full visual data-link implementation.
- Full bookmark store/index implementation.
- Full distributed recovery implementation.
- Full dispatch outbox processor.
- Full workflow-as-activity nested execution.
- Elsa 3 live instance compatibility.
- Complete activity catalog or Studio API migration.

## Work Sequence

### Slice 1. Executable Artifact Contract

Goal: define what Runtime executes.

Decisions to encode:

- Runtime executes a runtime-owned executable artifact.
- Workflow executions pin to an exact artifact snapshot by default.
- Authored documents and API/Studio shapes are compile/publish inputs, not runtime execution inputs.

Expected outputs:

- Concrete artifact contract or placeholder model name.
- Artifact identity fields: artifact ID, definition ID, definition version/source reference, artifact version/hash, created/published timestamp, compatibility metadata.
- Runtime executable node model or explicit decision that the first artifact shape does not need a separate node model yet.
- Artifact-level activity descriptor/reference shape.
- Artifact-level resume target table placeholder.
- Tests or structural checks proving runtime execution code does not depend on Design-owned authored models.

Key acceptance checks:

- A runtime service can load a minimal executable artifact without loading `WorkflowDefinitionState`.
- A workflow execution can reference the pinned artifact snapshot.
- Missing runtime activity support is reported as an executable-artifact/runtime dependency error, not as a Design deserialization error.

### Slice 2. WorkflowExecution And ActivityExecution State

Goal: define the durable execution identity and state contracts.

Decisions to encode:

- Split runtime state model.
- `ActivityExecution` is first-class durable identity.
- Evaluated inputs and raw outputs are not durable `ActivityExecution` state by default.

Expected outputs:

- `WorkflowExecutionState` model.
- `ActivityExecution` identity model.
- `ActivityExecutionState` lifecycle model.
- Relationship fields for scheduling, parent execution scope, branch, iteration, and call-stack depth.
- Minimal `SchedulerState` references to scheduled `ActivityExecution`s or executable nodes.
- State persistence interfaces or repositories if needed by the slice.

Key acceptance checks:

- A workflow instance can create a root workflow execution state pinned to an artifact.
- Scheduling an executable node creates or references an `ActivityExecution`.
- Loops/parallelism can be represented without relying only on authored activity ID.
- State contracts separate execution identity from history/audit payloads.

### Slice 3. Checkpoint Contract And Persistence Policy

Goal: make runtime commit boundaries explicit.

Decisions to encode:

- Checkpoints are named runtime boundaries.
- Checkpoint semantics and persistence policy are separate.
- Post-commit intents are recorded before commit and delivered only after commit succeeds.

Expected outputs:

- Checkpoint model with names from the decision report.
- Checkpoint writer/dispatcher abstraction.
- Default persistence policy.
- Atomic state-change envelope for workflow state, activity state, bookmarks, durable values, incidents, and operational markers.
- Post-commit intent placeholder contract.

Key acceptance checks:

- Runtime can produce checkpoints for workflow start, activity scheduled, activity started, activity completed, workflow suspended, workflow completed, and incident recorded.
- Tests show persistence policy can flush immediately or defer without changing checkpoint semantics.
- Post-commit intents are not delivered before successful checkpoint commit.

### Slice 4. Pipeline Slots And Inspectable Plans

Goal: define safe extension points before implementing behavior-heavy middleware.

Decisions to encode:

- Keep separate workflow and activity pipelines.
- Use stable named slots.
- Make resolved pipeline plans inspectable.

Expected outputs:

- Workflow pipeline builder with locked slot names.
- Activity pipeline builder with locked slot names.
- Middleware registration contract using slot and order.
- Optional before/after constraint support or explicit deferral.
- Pipeline plan introspection API/model.
- Minimal built-in middleware placeholders for load state, scheduling, input evaluation, invoke, capture outputs, checkpoint, and post-commit phases.

Key acceptance checks:

- A module can register middleware into a stable slot without depending on concrete neighboring middleware.
- The resolved pipeline order can be inspected in tests.
- Workflow and activity contexts remain distinct.

### Slice 5. Bookmark Resume Contract

Goal: define durable resume without persisting C# callback method names.

Decisions to encode:

- Bookmarks store `ResumeTargetId`.
- The pinned executable artifact maps resume target IDs to runtime handlers.

Expected outputs:

- Bookmark state model.
- Resume target declaration contract for activity authors.
- Artifact resume target table.
- Bookmark lookup fields: workflow execution ID, activity execution ID, executable node ID, stimulus type/hash.
- Resume resolution service.

Key acceptance checks:

- A bookmark can be created for an activity execution and executable node.
- Resume resolves through artifact resume target ID, not method name stored in the bookmark.
- Missing resume target produces a clear artifact compatibility/runtime feature error.

### Slice 6. Input Bindings, Outputs, And Durable Value Capture

Goal: connect expression/input behavior to the value-persistence decisions.

Decisions to encode:

- Data links compile to input bindings.
- Raw activity outputs are active-scope values only.
- Cross-suspension or ambiguous scope requires declared durable value capture.

Expected outputs:

- Runtime input binding model.
- Active execution output register scoped by `ActivityExecutionId`.
- Binding resolver rules for activity output, durable value, expression, literal, and reference values.
- Compile-time diagnostics for ambiguous output references.
- Durable value capture checkpoint integration.

Key acceptance checks:

- A same-scope activity can consume a prior output through a compiled binding.
- A binding that crosses suspension requires durable value capture.
- Loop/parallel ambiguity is rejected unless explicit semantics are declared.
- History output snapshots cannot be read as runtime input sources.

### Slice 7. Diagnostics, History, And Incidents

Goal: emit useful observability without making history runtime state.

Decisions to encode:

- Runtime state is continuation state only.
- History/audit payloads are policy-controlled.
- Incidents have minimal runtime state plus richer history/audit projection.

Expected outputs:

- Execution history event categories.
- Activity lifecycle and workflow lifecycle event models.
- Incident state model and incident history projection.
- Sensitive-value exclusion defaults.
- Payload capture policy contract.

Key acceptance checks:

- Runtime can continue without reading history records.
- Blocking incidents are queryable without replaying history.
- Input/output snapshots are omitted by default unless policy allows capture.

### Slice 8. Operational Recovery And Post-Commit Outbox

Goal: preserve Elsa 3 reliability guarantees with clean names and contracts.

Decisions to encode:

- Execution lease, heartbeat, graceful drain, interrupted execution, recovery scanner, post-commit outbox, and domain retry are distinct.
- Operational recovery is not domain retry.

Expected outputs:

- Execution lease contract.
- Heartbeat contract.
- Interrupted execution marker/state.
- Recovery scanner abstraction.
- Post-commit outbox item and delivery contract.
- Domain retry policy boundary.

Key acceptance checks:

- A lost lease can requeue from the last checkpoint without marking the activity as a domain retry.
- Post-commit intents follow record, commit, deliver, mark-delivered ordering.
- Drain/quiescence behavior can stop new work without corrupting active execution state.

### Slice 9. Elsa 3 Definition Migration Boundary

Goal: make compatibility explicit and bounded.

Decisions to encode:

- Elsa 4 supports Elsa 3 definition/document migration.
- Elsa 4 does not promise transparent live instance resume by default.

Expected outputs:

- Accepted Elsa 3 authored definition input shapes.
- Migration diagnostics model.
- Mapping from Elsa 3 authored definitions to Elsa 4 authored documents or executable compile inputs.
- Explicit unsupported-instance-resume documentation.
- Optional compatibility-host/tooling backlog if needed.

Key acceptance checks:

- Elsa 3 definition JSON can be imported or rejected with actionable diagnostics.
- Persisted Elsa 3 `WorkflowState` is not accepted as an Elsa 4 runtime execution state by accident.
- Users get clear cutover guidance for draining or externally migrating live Elsa 3 instances.

## Cross-Cutting Test Expectations

Every implementation slice should include focused tests where it introduces logic-bearing code.

Expected test categories:

- Structural dependency tests for Runtime not depending on Design execution-time models.
- Artifact pinning and identity tests.
- Runtime state serialization/deserialization tests for the new state contracts.
- Activity execution identity tests for loop and parallel scenarios.
- Checkpoint ordering and persistence-policy tests.
- Pipeline slot ordering and introspection tests.
- Bookmark resume target resolution tests.
- Active-scope output binding and durable capture tests.
- Incident/history separation tests.
- Operational recovery and outbox ordering tests.

## Risks To Keep Visible

- Starting with scheduler behavior before artifact/state contracts are pinned will recreate Elsa 3 coupling.
- Reusing Elsa 3 `WorkflowState` shape will undermine the split-state decision.
- Treating history as runtime state will make observability a hidden execution dependency.
- Persisting callback method names will undermine artifact-level resume target stability.
- Attempting live Elsa 3 instance resume will pull arbitrary serializer/object compatibility into the core runtime.
- Deferring pipeline slot design until after middleware exists will make module extension ordering brittle again.

## Suggested Immediate Next Action

Create a Speckit specification for:

```text
Runtime executable artifact and execution state contract
```

Use this action plan and the locked decision report as the specification inputs. The spec should deliberately stop before full scheduler/bookmark/outbox implementation unless the artifact, state, and checkpoint contracts are already clear enough to support them.
