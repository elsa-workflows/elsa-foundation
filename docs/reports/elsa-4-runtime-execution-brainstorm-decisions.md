# Elsa 4 Runtime Execution Brainstorm Decisions

Status: brainstorm decisions locked for the Runtime Execution Seam discussion. This is not yet a ratified Speckit spec, implementation plan, glossary entry, or constitution gate.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Source evidence: [Elsa Core runtime execution layer analysis](elsa-core-runtime-execution-layer-analysis.md).

Related decisions: [Elsa 4 runtime serialization brainstorm decisions](elsa-4-runtime-serialization-brainstorm-decisions.md).

Parent queue: [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md).

## Purpose

Capture the Elsa 4 execution-layer direction selected during the interactive runtime execution review so the next planning/specification step can proceed without re-litigating the source-backed brainstorm decisions.

## Locked Decisions

### 1. Executable Artifact Boundary

Elsa 4 Runtime executes a runtime-owned executable artifact, not the authored workflow document directly.

Workflow instances default to exact artifact snapshot pinning. A running instance resumes against the compiled artifact it started with. Moving an active instance to a newer artifact requires an explicit migration or upgrade operation.

Implications:

- The authored document remains the stable Design-owned authoring, import, export, and Studio shape.
- Compile/publish produces the Runtime-owned executable artifact.
- Runtime start/resume should not load authored workflow documents or Studio/API JSON to decide what to execute.
- Descriptor resolution, expression compilation/binding, data-link compilation, workflow-as-activity references, validation, and missing dependency diagnostics happen before execution.
- Runtime migration between artifact versions is explicit, inspectable, and optional.

Rationale:

- Deterministic resume requires pinning each running instance to the executable artifact it started with.
- Runtime should not inherit authoring/API shape churn.
- Compatibility upgrades should be deliberate operations, not implicit guesses at resume time.

### 2. Runtime State Model

Elsa 4 will not model runtime persistence as one broad Elsa 3-style `WorkflowState`.

The runtime model is conceptually split into:

- `WorkflowExecutionState`
- `SchedulerState`
- `ActivityExecutionState`
- `BookmarkState`
- `DurableValueState`
- `IncidentState`
- `OperationalState`
- history/audit projections outside runtime state

Storage may persist these together for efficiency, but the contract stays separated.

Implications:

- `WorkflowExecutionState` owns identity, artifact reference, status/substatus, timestamps, correlation, parent workflow, tenant/system metadata.
- `SchedulerState` owns pending work, suspended/resumable work, queue position, and branch/iteration scheduling metadata.
- `ActivityExecutionState` owns active or resumable activity executions.
- `BookmarkState` owns durable resume handles and lookup fields.
- `DurableValueState` owns declared durable values only: variables, workflow inputs/outputs, captured activity outputs, and external references.
- `IncidentState` owns unresolved execution-affecting incidents.
- `OperationalState` owns host/runtime coordination markers such as outbox item IDs, drain/interruption markers, leases, and heartbeat data.
- History and audit records are observability projections, not runtime continuation state.

Rationale:

Each piece of state should declare why it exists: execution, scheduling, resumption, durable data, failure handling, operations, or diagnostics.

### 3. ActivityExecution Contract

Elsa 4 uses `ActivityExecution` as the durable identity for one concrete execution of one executable activity node.

Core identity:

```csharp
ActivityExecutionId
WorkflowExecutionId
ExecutableNodeId
AuthoredActivityId
ActivityType
ActivityTypeVersion
```

Core lifecycle:

```csharp
Status
SubStatus?
ScheduledAt
StartedAt
CompletedAt?
```

Execution relationship:

```csharp
SchedulingActivityExecutionId?
ParentActivityExecutionId?
BranchId?
IterationId?
CallStackDepth?
```

Associated state references:

```csharp
BookmarkIds
IncidentIds
FaultCount
AggregateFaultCount
Metadata
```

Implications:

- `ExecutableNodeId` identifies the compiled runtime node.
- `AuthoredActivityId` links back to the authored document/Studio activity.
- `ActivityExecutionId` identifies this specific run, especially inside loops and parallel branches.
- `SchedulingActivityExecutionId` answers what caused this activity to run.
- `ParentActivityExecutionId` answers which execution scope owns this activity execution.
- Evaluated inputs and raw outputs are not durable `ActivityExecution` state by default.

Rationale:

Elsa 3's `ActivityExecutionContext` mixes durable identity, live execution services, variables, scheduling, bookmarks, input/output access, and fault tracking. Elsa 4 should preserve the durable identity and relationship concepts while separating them from live execution context behavior.

### 4. Checkpoint Contract

Elsa 4 models checkpoints as named runtime boundaries where state changes and post-commit intents may be atomically persisted.

Core checkpoint names:

```text
WorkflowStarted
ActivityScheduled
ActivityStarted
ActivityCompleted
ActivitySuspended
BookmarkCreated
BookmarkConsumed
DurableValueCaptured
IncidentRecorded
WorkflowSuspended
WorkflowCompleted
WorkflowFaulted
WorkflowCancelled
PostCommitIntentRecorded
```

Checkpoint semantics are separate from persistence policy:

```text
Checkpoint boundary = what changed and why
Persistence policy = when/how to flush it
```

Implications:

- Not every checkpoint must flush to storage immediately.
- Storage policy can persist immediately, batch, or skip when safe.
- Middleware can still expose before/after hooks, but canonical checkpoint names describe durable runtime facts.
- Post-commit side effects are recorded before commit and delivered only after the checkpoint commit succeeds.

Rationale:

Elsa 3 commits at important moments, but the boundaries are distributed across middleware and commit handlers. Elsa 4 should make checkpoint semantics explicit and observable.

### 5. Bookmark Resume Contract

Elsa 4 bookmarks store stable executable-artifact resume targets, not C# callback method names.

Durable bookmark shape:

```csharp
BookmarkId
WorkflowExecutionId
ActivityExecutionId
ExecutableNodeId
ResumeTargetId
StimulusType
StimulusHash
Payload?
Metadata
CreatedAt
ExpiresAt?
```

Resolution model:

```text
Bookmark.ResumeTargetId
  -> pinned executable artifact resume table
  -> activity runtime handler
  -> C# method/delegate/function
```

Implications:

- `ActivityExecutionId` identifies the suspended execution that owns the bookmark.
- `ExecutableNodeId` identifies the compiled node.
- `ResumeTargetId` identifies the compiled resume target inside the pinned executable artifact.
- `StimulusType` and `StimulusHash` support external event lookup.
- `Payload` follows runtime value rules.
- `Metadata` is query/audit data, not arbitrary runtime state.
- C# activity authors may map stable resume target IDs to handlers through attributes, descriptor registration, or another runtime activity contract.

Example:

```csharp
[ResumeTarget("wait-for-delivery")]
public ValueTask OnDeliveryStatusReceived(ActivityResumeContext context)
```

Rationale:

`ResumeTargetId` is the durable contract. Method names and delegates are implementation details. Activity authors can refactor C# code as long as they preserve the declared resume target IDs.

### 6. Pipeline Extension Model

Elsa 4 keeps two distinct pipelines:

```text
WorkflowExecutionPipeline
ActivityExecutionPipeline
```

They share infrastructure where useful, but keep separate context models.

Workflow pipeline slots:

```text
Ingress
LoadExecutionState
AcquireExecutionLease
BeforeRun
Schedule
Checkpoint
PostCommit
CompleteRun
ReleaseExecutionLease
```

Activity pipeline slots:

```text
BeforeActivity
EvaluateInputs
BeforeInvoke
Invoke
AfterInvoke
CaptureOutputs
HandleActivityResult
Checkpoint
AfterActivity
```

Middleware targets stable slots:

```csharp
builder.ActivityPipeline.Add<ValidateTenantAccessMiddleware>(
    slot: ActivityPipelineSlot.BeforeInvoke,
    order: 50);
```

Implications:

- Slot names are stable extension contracts.
- Optional `Before` and `After` constraints may exist for advanced cases.
- Module developers should normally target slots and coarse order instead of exact middleware neighbors.
- The resolved pipeline plan must be inspectable for diagnostics.
- The model should not force one generic pipeline if doing so weakens workflow or activity context clarity.

Rationale:

The problem in Elsa 3 is not simply that two pipelines exist. The problem is behavior-critical ordering encoded through implicit linked middleware. Elsa 4 should keep the separate lifecycle contexts but make extension points named, inspectable, and safer to target.

### 7. Output And Data-Link Runtime Semantics

Elsa 4 distinguishes:

```text
Activity output
Data link
Durable value
```

A data link compiles into an input binding. It may read directly from an activity output only within the same active execution scope.

Runtime rules:

- Raw activity outputs are scoped and ephemeral.
- Values crossing suspension or resume require capture into declared durable values.
- Values crossing uncertain branch, loop, or parallel boundaries require explicit semantics.
- Ambiguous output references are compile-time errors unless scoped.
- History/audit output snapshots are not runtime data sources.

Examples of required explicit semantics:

```text
last(A.Output)
all(A.Output)
iteration(A.Output, current)
capture A.Output into Customers[]
```

Rationale:

Elsa 3 can look up activity outputs by activity ID or activity execution context ID. Activity ID lookup becomes ambiguous in loops/parallelism, and outputs disappear when completed execution contexts are cleared. Elsa 4 should keep raw outputs useful within active execution scope while requiring declared durable capture for values that must survive uncertain scopes.

### 8. Diagnostics, History, And Incidents

Elsa 4 separates:

```text
Runtime state
Execution history
Audit/diagnostic payloads
Incident state
```

Rules:

- Runtime state is only what the engine needs to continue execution.
- Runtime does not read from history to continue.
- History may store selected input/output/value snapshots for observability only.
- Sensitive values are excluded by default.
- Payload capture is policy-driven.
- Blocking incidents remain queryable without replaying history.
- Incidents have minimal first-class runtime state plus richer history/audit records.

Event categories:

```text
WorkflowLifecycle
ActivityLifecycle
BookmarkLifecycle
ValueLifecycle
IncidentLifecycle
SchedulerLifecycle
OperationalLifecycle
```

Rationale:

Elsa 3 already persists activity execution records and workflow execution logs separately from workflow instance state. Elsa 4 should formalize that split and avoid making history the continuation source.

### 9. Operational Recovery And Outbox

Elsa 4 distinguishes:

```text
Execution lease
Heartbeat
Graceful drain
Interrupted execution
Recovery scanner
Post-commit outbox
Domain retry
```

Rules:

- Operational recovery is not domain retry.
- Lost or cancelled host execution may requeue from the last checkpoint.
- Domain retry only happens when workflow/activity policy says so.
- Post-commit effects use:

```text
record intent -> checkpoint commit succeeds -> deliver intent -> mark delivered
```

Examples of post-commit intents:

```text
dispatch child workflow
send runtime command
schedule external background continuation
publish integration event
```

Implications:

- Operational recovery and outbox belong to runtime infrastructure.
- Domain retry belongs to workflow/activity policy.
- Post-commit intent delivery is not ordinary workflow variable persistence.

Rationale:

Elsa 3's interrupted recovery and outbox behavior preserve useful reliability guarantees, but these operational concepts should be named separately from workflow logic and failure handling.

### 10. Elsa 3 Compatibility Boundary

Elsa 4 supports Elsa 3 definition/document migration. Elsa 4 does not promise transparent live resume of arbitrary Elsa 3 workflow instances by default.

Supported:

- Import Elsa 3 workflow definitions.
- Migrate authored JSON/document shapes.
- Compile imported definitions into Elsa 4 executable artifacts.
- Provide migration diagnostics and fixups.
- Optionally provide tools to inspect/export Elsa 3 instances before cutover.

Not supported by default:

- Binary/object-level compatibility with Elsa 3 `WorkflowState`.
- Transparent resume of Elsa 3 persisted activity execution contexts.
- Automatic mapping of Elsa 3 callback-method bookmarks to Elsa 4 `ResumeTargetId`.
- Preserving every arbitrary serialized runtime object shape.

Optional bridge:

- A separate Elsa 3 compatibility host or migration tool may help drain existing Elsa 3 instances, export business state, or restart selected workflows in Elsa 4 with explicit mappings.

Rationale:

Elsa 3 live instance state includes object-heavy workflow state, active activity execution contexts, scheduled work items, callback method names, bookmarks, arbitrary values, serializer-specific shapes, runtime graph assumptions, and custom activity details. Carrying that into Elsa 4 Runtime would force the new runtime seam to preserve too much Elsa 3 machinery.

## Open Questions For Specification

- What is the concrete name and shape of the runtime-owned executable artifact?
- Does the executable artifact contain a runtime graph, serialized runtime node descriptors, constructed activity factories, or a staged representation?
- What exact storage shape should represent the split runtime state areas?
- Which checkpoint boundaries flush immediately in the default persistence policy?
- How are resume target IDs declared by activity authors and validated during compile/publish?
- What is the minimal execution lease and heartbeat model for a single-node host versus distributed hosts?
- Which execution history events are always emitted, and which payloads are policy-controlled?
- Which Elsa 3 definition shapes are accepted by the migration/import tool?

## Follow-Up Surface

These decisions should feed the next report/specification layer:

- [Elsa 4 runtime execution actionable plan](elsa-4-runtime-execution-action-plan.md)
- A later Speckit specification for the executable artifact and first runtime execution slice.
